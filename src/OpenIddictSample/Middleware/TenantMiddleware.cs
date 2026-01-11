namespace OpenIddictSample.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract tenant ID from header, subdomain, or query string
        string? tenantId = null;

        // Try to get from X-Tenant-ID header
        if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var headerValue))
        {
            tenantId = headerValue.ToString();
        }
        // Try to get from query string
        else if (context.Request.Query.TryGetValue("tenant", out var queryValue))
        {
            tenantId = queryValue.ToString();
        }
        // Default tenant for development
        else
        {
            tenantId = "default";
        }

        if (!string.IsNullOrEmpty(tenantId))
        {
            context.Items["TenantId"] = tenantId;
            _logger.LogInformation("Request for tenant: {TenantId}", tenantId);
        }

        await _next(context);
    }
}

public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TenantMiddleware>();
    }
}
