using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;
using System.Security.Claims;
using System.Text.Json;
using OpenIddictSample.Services;

namespace OpenIddictSample.Controllers;

/// <summary>
/// BFF (Backend For Frontend) Controller
/// Provides secure authentication endpoints for SPAs and mobile apps
/// Tokens are stored server-side, only secure session cookies exposed to frontend
/// </summary>
[ApiController]
[Route("bff")]
public class BffController(
    IBffSessionService sessionService,
    ITenantService tenantService,
    ILogger<BffController> logger
)
    : ControllerBase
{
    /// <summary>
    /// Login endpoint for BFF pattern
    /// POST /bff/login
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request
    )
    {
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { error = "username_and_password_required" });
        }

        var tenantId = tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tenantId))
        {
            tenantId = request.TenantId ?? "tenant1";
        }

        // Validate tenant
        if (!await tenantService.ValidateTenantAsync(tenantId))
        {
            return BadRequest(new { error = "invalid_tenant" });
        }

        // Here you would validate credentials against your user store
        // For now, this is a simplified example
        // In production, integrate with AccountController login logic

        // Create session
        var sessionId = Guid.NewGuid().ToString();
        var session = new BffSession
        {
            UserId = Guid.NewGuid().ToString(), // Replace with actual user ID
            Username = request.Username,
            Email = $"{request.Username}@example.com", // Replace with actual email
            TenantId = tenantId,
            AccessToken = "dummy_access_token", // Replace with actual token from OAuth flow
            RefreshToken = "dummy_refresh_token",
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(30),
            CreatedAt = DateTime.UtcNow
        };

        await sessionService.CreateSessionAsync(sessionId, session);

        // Set secure HTTP-only cookie
        Response.Cookies.Append(
            "bff_session",
            sessionId,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromHours(12)
            }
        );

        return Ok(
            new
            {
                success = true,
                user = new
                {
                    userId = session.UserId,
                    username = session.Username,
                    email = session.Email,
                    tenantId = session.TenantId
                }
            }
        );
    }

    /// <summary>
    /// Logout endpoint
    /// POST /bff/logout
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies.TryGetValue("bff_session", out var sessionId))
        {
            await sessionService.DeleteSessionAsync(sessionId);
            Response.Cookies.Delete("bff_session");
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Get current user information
    /// GET /bff/user
    /// </summary>
    [HttpGet("user")]
    public async Task<IActionResult> GetUser()
    {
        if (!Request.Cookies.TryGetValue("bff_session", out var sessionId))
        {
            return Unauthorized(new { error = "no_session" });
        }

        var session = await sessionService.GetSessionAsync(sessionId);
        if (session == null)
        {
            Response.Cookies.Delete("bff_session");
            return Unauthorized(new { error = "session_expired" });
        }

        // Check if access token is expired
        if (session.AccessTokenExpiresAt <= DateTime.UtcNow)
        {
            // Try to refresh
            var refreshed = await sessionService.RefreshSessionTokensAsync(sessionId);
            if (!refreshed)
            {
                await sessionService.DeleteSessionAsync(sessionId);
                Response.Cookies.Delete("bff_session");
                return Unauthorized(new { error = "session_expired" });
            }

            // Get updated session
            session = await sessionService.GetSessionAsync(sessionId);
        }

        return Ok(
            new
            {
                userId = session?.UserId ?? string.Empty,
                username = session?.Username ?? string.Empty,
                email = session?.Email ?? string.Empty,
                tenantId = session?.TenantId ?? string.Empty,
                authenticated = true
            }
        );
    }

    /// <summary>
    /// API Proxy endpoint - forwards requests to backend API with access token
    /// Allows frontend to call APIs without handling tokens
    /// GET/POST/PUT/DELETE /bff/api/{*path}
    /// </summary>
    [HttpGet("api/{**path}")]
    [HttpPost("api/{**path}")]
    [HttpPut("api/{**path}")]
    [HttpDelete("api/{**path}")]
    [HttpPatch("api/{**path}")]
    public async Task<IActionResult> ApiProxy(
        string path
    )
    {
        if (!Request.Cookies.TryGetValue("bff_session", out var sessionId))
        {
            return Unauthorized(new { error = "no_session" });
        }

        var session = await sessionService.GetSessionAsync(sessionId);
        if (session == null)
        {
            return Unauthorized(new { error = "session_expired" });
        }

        // Check if token needs refresh
        if (session.AccessTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
        {
            await sessionService.RefreshSessionTokensAsync(sessionId);
            session = await sessionService.GetSessionAsync(sessionId);

            if (session == null)
            {
                return Unauthorized(new { error = "session_refresh_failed" });
            }
        }

        // Forward request to actual API
        using var client = new HttpClient();
        var apiBaseUrl = Request.Scheme + "://" + Request.Host;
        var targetUrl = $"{apiBaseUrl}/api/{path}";

        var requestMessage = new HttpRequestMessage
        {
            Method = new HttpMethod(Request.Method),
            RequestUri = new Uri(targetUrl)
        };

        // Add access token to authorization header
        requestMessage.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);

        // Copy request body if present
        if (Request.ContentLength > 0)
        {
            var streamContent = new StreamContent(Request.Body);
            foreach (var header in Request.Headers)
            {
                if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    streamContent.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            requestMessage.Content = streamContent;
        }

        try
        {
            var response = await client.SendAsync(requestMessage);
            var content = await response.Content.ReadAsStringAsync();

            return new ContentResult
            {
                Content = content,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                StatusCode = (int)response.StatusCode
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error proxying request to API");
            return StatusCode(500, new { error = "proxy_error" });
        }
    }

    /// <summary>
    /// Check if user is authenticated
    /// GET /bff/auth/check
    /// </summary>
    [HttpGet("auth/check")]
    public async Task<IActionResult> CheckAuth()
    {
        if (!Request.Cookies.TryGetValue("bff_session", out var sessionId))
        {
            return Ok(new { authenticated = false });
        }

        var session = await sessionService.GetSessionAsync(sessionId);

        return Ok(new { authenticated = session != null });
    }

    /// <summary>
    /// Get CSRF token for state-changing operations
    /// GET /bff/antiforgery
    /// </summary>
    [HttpGet("antiforgery")]
    public IActionResult GetAntiforgeryToken()
    {
        var tokens = HttpContext.RequestServices
            .GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
            .GetAndStoreTokens(HttpContext);

        return Ok(
            new
            {
                token = tokens.RequestToken,
                headerName = "X-CSRF-TOKEN"
            }
        );
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? TenantId { get; set; }
}
