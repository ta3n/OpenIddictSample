namespace OpenIddictSample.Middleware;

/// <summary>
/// Middleware to add security headers for BFF pattern
/// </summary>
public class BffSecurityHeadersMiddleware(
    RequestDelegate next
)
{
    public async Task InvokeAsync(
        HttpContext context
    )
    {
        // Add security headers
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // Content Security Policy for BFF
        context.Response.Headers.Append(
            "Content-Security-Policy",
            "default-src 'self'; "
            + "script-src 'self' 'unsafe-inline'; "
            + "style-src 'self' 'unsafe-inline'; "
            + "img-src 'self' data: https:; "
            + "font-src 'self' data:; "
            + "connect-src 'self'; "
            + "frame-ancestors 'none'"
        );

        await next(context);
    }
}

public static class BffSecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseBffSecurityHeaders(
        this IApplicationBuilder builder
    )
    {
        return builder.UseMiddleware<BffSecurityHeadersMiddleware>();
    }
}
