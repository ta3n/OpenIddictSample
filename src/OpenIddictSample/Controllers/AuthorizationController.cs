using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddictSample.Data;
using OpenIddictSample.Services;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictSample.Controllers;

[ApiController]
public class AuthorizationController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly RedisTokenService _redisTokenService;
    private readonly PasswordHasher _passwordHasher;
    private readonly ILogger<AuthorizationController> _logger;

    public AuthorizationController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        IOpenIddictTokenManager tokenManager,
        ApplicationDbContext dbContext,
        RedisTokenService redisTokenService,
        PasswordHasher passwordHasher,
        ILogger<AuthorizationController> logger)
    {
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _tokenManager = tokenManager;
        _dbContext = dbContext;
        _redisTokenService = redisTokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Get tenant from context
        var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "default";

        // Retrieve the user principal stored in the authentication cookie
        var result = await HttpContext.AuthenticateAsync();

        // For simplicity, auto-create a test user if not authenticated
        if (!result.Succeeded)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Username == "testuser" && u.TenantId == tenantId);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    Username = "testuser",
                    PasswordHash = _passwordHasher.HashPassword("password"),
                    TenantId = tenantId
                };
                _dbContext.Users.Add(user);
                await _dbContext.SaveChangesAsync();
            }

            var claims = new List<Claim>
            {
                new Claim(Claims.Subject, user.Id),
                new Claim(Claims.Name, user.Username),
                new Claim("tenant_id", tenantId)
            };

            var claimsIdentity = new ClaimsIdentity(claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            result = AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, 
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
        }

        var principal = result.Principal;

        // Retrieve the application details from the database
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The application details cannot be found.");

        // Retrieve the permanent authorizations associated with the user and the calling client application
        var authorizationsEnum = _authorizationManager.FindAsync(
            subject: principal!.GetClaim(Claims.Subject)!,
            client: await _applicationManager.GetIdAsync(application)!,
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes());

        var authorizations = new List<object>();
        await foreach (var auth in authorizationsEnum)
        {
            authorizations.Add(auth);
        }

        // Create authorization if needed
        var authorization = authorizations.LastOrDefault();
        if (authorization == null)
        {
            authorization = await _authorizationManager.CreateAsync(
                principal: principal!,
                subject: principal.GetClaim(Claims.Subject)!,
                client: await _applicationManager.GetIdAsync(application)!,
                type: AuthorizationTypes.Permanent,
                scopes: principal.GetScopes());
        }

        principal!.SetScopes(request.GetScopes());
        
        var resourcesEnum = _scopeManager.ListResourcesAsync(principal.GetScopes());
        var resources = new List<string>();
        await foreach (var resource in resourcesEnum)
        {
            resources.Add(resource);
        }
        principal.SetResources(resources);

        // Set authorization id
        principal.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));

        // Set destinations for claims
        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim, principal));
        }

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "default";

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // Retrieve the claims principal stored in the authorization code/refresh token
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // Retrieve the user profile corresponding to the authorization code/refresh token
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => 
                u.Id == result.Principal!.GetClaim(Claims.Subject) && 
                u.TenantId == tenantId);

            if (user == null)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                    }));
            }

            // Handle refresh token rotation
            if (request.IsRefreshTokenGrantType())
            {
                var oldRefreshToken = await _dbContext.RefreshTokens
                    .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken && rt.TenantId == tenantId);

                if (oldRefreshToken != null)
                {
                    // Check if token is revoked
                    if (oldRefreshToken.IsRevoked)
                    {
                        _logger.LogWarning("Attempted use of revoked refresh token");
                        return Forbid(
                            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                            properties: new AuthenticationProperties(new Dictionary<string, string?>
                            {
                                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token has been revoked."
                            }));
                    }

                    // Check expiration
                    if (oldRefreshToken.ExpiresAt < DateTime.UtcNow)
                    {
                        return Forbid(
                            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                            properties: new AuthenticationProperties(new Dictionary<string, string?>
                            {
                                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token has expired."
                            }));
                    }

                    // Revoke old token (rotation)
                    oldRefreshToken.IsRevoked = true;
                    var newRefreshTokenValue = Guid.NewGuid().ToString();
                    oldRefreshToken.ReplacedByToken = newRefreshTokenValue;

                    // Create new refresh token
                    var newRefreshToken = new RefreshToken
                    {
                        Token = newRefreshTokenValue,
                        UserId = user.Id,
                        TenantId = tenantId,
                        ExpiresAt = DateTime.UtcNow.AddDays(30)
                    };
                    _dbContext.RefreshTokens.Add(newRefreshToken);
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation("Refresh token rotated for user {UserId} in tenant {TenantId}", user.Id, tenantId);
                }
            }

            // Create a new ClaimsPrincipal containing the claims that will be used to create tokens
            var principal = result.Principal!;

            // Set destinations for claims
            foreach (var claim in principal.Claims)
            {
                claim.SetDestinations(GetDestinations(claim, principal));
            }

            // Store access token in Redis
            var accessToken = Guid.NewGuid().ToString();
            await _redisTokenService.StoreTokenAsync(
                $"access_token:{accessToken}",
                new { UserId = user.Id, TenantId = tenantId, Claims = principal.Claims.Select(c => new { c.Type, c.Value }) },
                TimeSpan.FromHours(1));

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsPasswordGrantType())
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => 
                u.Username == request.Username && 
                u.TenantId == tenantId);

            if (user == null || !_passwordHasher.VerifyPassword(user.PasswordHash, request.Password!))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username or password is invalid."
                    }));
            }

            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            identity.AddClaim(Claims.Subject, user.Id);
            identity.AddClaim(Claims.Name, user.Username);
            identity.AddClaim("tenant_id", tenantId);

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());

            // Set destinations for claims
            foreach (var claim in principal.Claims)
            {
                claim.SetDestinations(GetDestinations(claim, principal));
            }

            // Create refresh token
            var refreshToken = new RefreshToken
            {
                Token = Guid.NewGuid().ToString(),
                UserId = user.Id,
                TenantId = tenantId,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            _dbContext.RefreshTokens.Add(refreshToken);
            await _dbContext.SaveChangesAsync();

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    [HttpPost("~/connect/revoke")]
    public async Task<IActionResult> Revoke()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "default";

        // Try to revoke the token
        if (!string.IsNullOrEmpty(request.Token))
        {
            // Revoke from database
            var refreshToken = await _dbContext.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == request.Token && rt.TenantId == tenantId);

            if (refreshToken != null)
            {
                refreshToken.IsRevoked = true;
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Refresh token revoked for tenant {TenantId}", tenantId);
            }

            // Revoke from Redis
            await _redisTokenService.DeleteTokenAsync($"access_token:{request.Token}");
        }

        return Ok();
    }

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "default";
        
        // Revoke all tokens for the current user
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Succeeded)
        {
            var userId = result.Principal?.GetClaim(Claims.Subject);
            if (!string.IsNullOrEmpty(userId))
            {
                var refreshTokens = await _dbContext.RefreshTokens
                    .Where(rt => rt.UserId == userId && rt.TenantId == tenantId && !rt.IsRevoked)
                    .ToListAsync();

                foreach (var token in refreshTokens)
                {
                    token.IsRevoked = true;
                }
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("All tokens revoked for user {UserId} in tenant {TenantId}", userId, tenantId);
            }
        }

        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
    {
        // Note: by default, claims are NOT automatically included in the access and identity tokens.
        // To allow OpenIddict to serialize them, you must attach them a destination
        switch (claim.Type)
        {
            case Claims.Name:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Profile))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Roles))
                    yield return Destinations.IdentityToken;

                yield break;

            case "tenant_id":
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            // Never include the security stamp in the access and identity tokens, as it's a secret value.
            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
