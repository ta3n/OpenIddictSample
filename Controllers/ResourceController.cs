using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddictSample2.Services;
using System.Security.Claims;

namespace OpenIddictSample2.Controllers;

/// <summary>
/// Protected API Controller - demonstrates token validation
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ResourceController(
    ITenantService tenantService
) : ControllerBase
{
    /// <summary>
    /// Get current user information from token
    /// Requires valid access token
    /// </summary>
    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var tenantId = User.FindFirst("tenant_id")?.Value;

        return Ok(
            new
            {
                UserId = userId,
                Username = username,
                Email = email,
                TenantId = tenantId,
                Claims = User.Claims.Select(c => new { c.Type, c.Value })
            }
        );
    }

    /// <summary>
    /// Protected endpoint - requires specific scope
    /// </summary>
    [HttpGet("data")]
    [Authorize(Policy = "ApiScope")]
    public IActionResult GetProtectedData()
    {
        var tenantId = tenantService.GetCurrentTenantId();

        return Ok(
            new
            {
                Message = "This is protected data",
                TenantId = tenantId,
                Timestamp = DateTime.UtcNow,
                Data = new[]
                {
                    "Item 1",
                    "Item 2",
                    "Item 3"
                }
            }
        );
    }

    /// <summary>
    /// Tenant-specific data endpoint
    /// Returns data only for the user's tenant
    /// </summary>
    [HttpGet("tenant-data")]
    public async Task<IActionResult> GetTenantData()
    {
        var userTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            return BadRequest(new { Error = "Tenant ID not found in token" });
        }

        if (!await tenantService.ValidateTenantAsync(userTenantId))
        {
            return Forbid("Invalid or inactive tenant");
        }

        return Ok(
            new
            {
                Message = $"Data for tenant {userTenantId}",
                TenantId = userTenantId,
                TenantSpecificData = new
                {
                    Setting1 = "Value1",
                    Setting2 = "Value2",
                    LastUpdated = DateTime.UtcNow
                }
            }
        );
    }

    /// <summary>
    /// Admin-only endpoint (example)
    /// </summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetAdminData()
    {
        return Ok(
            new
            {
                Message = "Admin-only data",
                Timestamp = DateTime.UtcNow
            }
        );
    }
}