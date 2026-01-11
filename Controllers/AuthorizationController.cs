using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddictSample2.Services;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictSample2.Controllers;

/// <summary>
/// OAuth 2.0 and OpenID Connect Authorization Controller
/// Implements:
/// - Authorization Code Flow
/// - Refresh Token Rotation
/// - Token Revocation
/// - Multi-Tenant Support
/// </summary>
public class AuthorizationController : Controller
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly ITenantService _tenantService;
    private readonly ITokenStorageService _tokenStorage;

    private const string TenantIdClaim = "tenant_id";

    public AuthorizationController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        ITenantService tenantService,
        ITokenStorageService tokenStorage)
    {
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _tenantService = tenantService;
        _tokenStorage = tokenStorage;
    }

    /// <summary>
    /// Authorization endpoint - handles authorization code flow
    /// GET /connect/authorize
    /// </summary>
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Get tenant context
        var tenantId = _tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidRequest,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Tenant ID is required."
                }));
        }

        // Validate tenant
        if (!await _tenantService.ValidateTenantAsync(tenantId))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidRequest,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid tenant."
                }));
        }

        // Try to authenticate the user
        var result = await HttpContext.AuthenticateAsync();

        // If user is not authenticated, redirect to login page
        if (!result.Succeeded)
        {
            return Challenge(
                authenticationSchemes: "Cookies",
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
                });
        }

        // Check if user belongs to the tenant
        var userTenantId = result.Principal.FindFirst("tenant_id")?.Value;
        if (userTenantId != tenantId)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidRequest,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User does not belong to the specified tenant."
                }));
        }

        // Retrieve the application details
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The application cannot be found.");

        // Create a new ClaimsIdentity
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        // Add user claims
        var userId = result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        identity.SetClaim(Claims.Subject, userId)
                .SetClaim(Claims.Name, result.Principal.FindFirst(Claims.Name)?.Value)
                .SetClaim(Claims.Email, result.Principal.FindFirst(Claims.Email)?.Value)
                .SetClaim(TenantIdClaim, tenantId);

        identity.SetDestinations(claim => claim.Type switch
        {
            Claims.Name or Claims.Email => new[] { Destinations.AccessToken, Destinations.IdentityToken },
            Claims.Subject => new[] { Destinations.AccessToken, Destinations.IdentityToken },
            var type when type == TenantIdClaim => new[] { Destinations.AccessToken, Destinations.IdentityToken },
            _ => new[] { Destinations.AccessToken }
        });

        var principal = new ClaimsPrincipal(identity);

        // Set requested scopes
        principal.SetScopes(request.GetScopes());

        // Set resources (if any)
        var resources = new List<string>();
        await foreach (var resource in _scopeManager.ListResourcesAsync(principal.GetScopes()))
        {
            resources.Add(resource);
        }
        principal.SetResources(resources);

        // Create authorization entry
        var authorizationId = await _authorizationManager.GetIdAsync(result.Principal);
        var authorization = !string.IsNullOrEmpty(authorizationId) 
            ? await _authorizationManager.FindByIdAsync(authorizationId)
            : null;
        
        authorization ??= await CreateAuthorizationAsync(principal, application);

        if (authorization != null)
        {
            principal.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));
        }

        // Return authorization response
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Token endpoint - handles token exchange and refresh
    /// POST /connect/token
    /// </summary>
    [HttpPost("~/connect/token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Handle authorization code flow
        if (request.IsAuthorizationCodeGrantType())
        {
            return await HandleAuthorizationCodeAsync();
        }

        // Handle refresh token with rotation
        if (request.IsRefreshTokenGrantType())
        {
            return await HandleRefreshTokenAsync();
        }

        // Handle client credentials
        if (request.IsClientCredentialsGrantType())
        {
            return await HandleClientCredentialsAsync(request);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    /// <summary>
    /// Logout endpoint
    /// POST /connect/logout
    /// </summary>
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        var request = HttpContext.GetOpenIddictServerRequest();

        // Sign out from the authentication scheme
        await HttpContext.SignOutAsync("Cookies");

        // Revoke all refresh tokens for the user
        var result = await HttpContext.AuthenticateAsync();
        if (result.Succeeded)
        {
            var userId = result.Principal.FindFirst(Claims.Subject)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await _tokenStorage.RevokeAllTokensForUserAsync(userId);
            }
        }

        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties
            {
                RedirectUri = request?.PostLogoutRedirectUri ?? "/"
            });
    }

    /// <summary>
    /// Token revocation endpoint
    /// POST /connect/revoke
    /// </summary>
    [HttpPost("~/connect/revoke")]
    public async Task<IActionResult> Revoke()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Extract token from request
        var token = request.Token;
        if (string.IsNullOrEmpty(token))
        {
            return BadRequest(new { error = "invalid_request" });
        }

        // Revoke the token
        await _tokenStorage.RevokeRefreshTokenAsync(token);

        return Ok();
    }

    #region Private Helper Methods

    private async Task<IActionResult> HandleAuthorizationCodeAsync()
    {
        var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal!;

        var userId = principal.FindFirst(Claims.Subject)?.Value!;
        var tenantId = principal.FindFirst("tenant_id")?.Value!;

        // Generate refresh token ID
        var refreshTokenId = Guid.NewGuid().ToString();

        // Store refresh token data in Redis
        await _tokenStorage.StoreRefreshTokenAsync(refreshTokenId, new RefreshTokenData
        {
            TokenId = refreshTokenId,
            UserId = userId,
            TenantId = tenantId,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            RotationCount = 0
        }, TimeSpan.FromDays(30));

        // Add refresh token ID to claims
        principal.SetClaim("refresh_token_id", refreshTokenId);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleRefreshTokenAsync()
    {
        var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal!;

        var oldRefreshTokenId = principal.FindFirst("refresh_token_id")?.Value;
        if (string.IsNullOrEmpty(oldRefreshTokenId))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid refresh token."
                }));
        }

        // Check if token is revoked
        if (await _tokenStorage.IsTokenRevokedAsync(oldRefreshTokenId))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Token has been revoked."
                }));
        }

        // Get stored token data
        var tokenData = await _tokenStorage.GetRefreshTokenAsync(oldRefreshTokenId);
        if (tokenData == null || tokenData.ExpiresAt < DateTime.UtcNow)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Refresh token expired."
                }));
        }

        // Revoke old refresh token
        await _tokenStorage.RevokeRefreshTokenAsync(oldRefreshTokenId);

        // Generate new refresh token with rotation
        var newRefreshTokenId = Guid.NewGuid().ToString();
        await _tokenStorage.StoreRefreshTokenAsync(newRefreshTokenId, new RefreshTokenData
        {
            TokenId = newRefreshTokenId,
            UserId = tokenData.UserId,
            TenantId = tokenData.TenantId,
            PreviousTokenId = oldRefreshTokenId,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            RotationCount = tokenData.RotationCount + 1
        }, TimeSpan.FromDays(30));

        // Update principal with new refresh token ID
        var identity = new ClaimsIdentity(principal.Identity);
        identity.SetClaim("refresh_token_id", newRefreshTokenId);

        var newPrincipal = new ClaimsPrincipal(identity);
        newPrincipal.SetScopes(principal.GetScopes());
        newPrincipal.SetResources(principal.GetResources());

        return SignIn(newPrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleClientCredentialsAsync(OpenIddictRequest request)
    {
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The application cannot be found.");

        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, await _applicationManager.GetClientIdAsync(application))
                .SetClaim(Claims.Name, await _applicationManager.GetDisplayNameAsync(application));

        identity.SetDestinations(static _ => new[] { Destinations.AccessToken });

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        
        var resources = new List<string>();
        await foreach (var resource in _scopeManager.ListResourcesAsync(principal.GetScopes()))
        {
            resources.Add(resource);
        }
        principal.SetResources(resources);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<object?> CreateAuthorizationAsync(
        ClaimsPrincipal principal,
        object application)
    {
        var clientId = await _applicationManager.GetIdAsync(application);
        if (string.IsNullOrEmpty(clientId))
        {
            throw new InvalidOperationException("Cannot create authorization: client ID is null");
        }

        var authorization = await _authorizationManager.CreateAsync(
            principal: principal,
            subject: principal.GetClaim(Claims.Subject) ?? throw new InvalidOperationException("Subject claim is required"),
            client: clientId,
            type: AuthorizationTypes.Permanent,
            scopes: principal.GetScopes());

        return authorization;
    }

    #endregion
}
