using Microsoft.EntityFrameworkCore;
using OpenIddictSample2.Data;

namespace OpenIddictSample2.Services;

/// <summary>
/// Service to manage tenant context in multi-tenant environment
/// </summary>
public interface ITenantService
{
    /// <summary>
    /// Gets the current tenant ID from the HTTP context.
    /// Attempts to retrieve tenant ID from header (X-Tenant-ID), subdomain, or user claims.
    /// </summary>
    /// <returns>The tenant ID if found; otherwise, null.</returns>
    string? GetCurrentTenantId();

    /// <summary>
    /// Validates whether a tenant exists and is active.
    /// </summary>
    /// <param name="tenantId">The tenant ID to validate.</param>
    /// <returns>True if the tenant exists and is active; otherwise, false.</returns>
    Task<bool> ValidateTenantAsync(
        string tenantId
    );
}

public class TenantService(
    IHttpContextAccessor httpContextAccessor,
    ApplicationDbContext context
)
    : ITenantService
{
    public string? GetCurrentTenantId()
    {
        // Extract tenant from header, subdomain, or claims
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return null;
        }

        // Try from header first
        if (httpContext.Request.Headers.TryGetValue("X-Tenant-ID", out var tenantId))
        {
            return tenantId.ToString();
        }

        // Try from subdomain
        var host = httpContext.Request.Host.Host;
        var parts = host.Split('.');
        if (parts.Length > 2)
        {
            return parts[0]; // subdomain as tenant ID
        }

        // Try from claims
        var tenantClaim = httpContext.User.FindFirst("tenant_id");
        return tenantClaim?.Value;
    }

    public async Task<bool> ValidateTenantAsync(
        string tenantId
    )
    {
        var tenant = await context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId && t.IsActive);

        return tenant != null;
    }
}