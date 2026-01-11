using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddictSample.Data;

namespace OpenIddictSample.Services;

public class DatabaseInitializerWorker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializerWorker> _logger;

    public DatabaseInitializerWorker(
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        // Create a test client application if it doesn't exist
        if (await manager.FindByClientIdAsync("test-client", cancellationToken) == null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                DisplayName = "Test Client Application",
                RedirectUris = { new Uri("https://localhost:7001/callback") },
                PostLogoutRedirectUris = { new Uri("https://localhost:7001/") },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Revocation,
                    OpenIddictConstants.Permissions.Endpoints.Logout,
                    
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.GrantTypes.Password,
                    
                    OpenIddictConstants.Permissions.Prefixes.Scope + "api",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "profile",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "email",
                    
                    OpenIddictConstants.Permissions.ResponseTypes.Code
                }
            }, cancellationToken);

            _logger.LogInformation("Test client application created successfully");
        }

        // Create scopes
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        
        if (await scopeManager.FindByNameAsync("api", cancellationToken) == null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "api",
                Resources = { "resource_server" }
            }, cancellationToken);
        }

        _logger.LogInformation("Database initialization completed");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
