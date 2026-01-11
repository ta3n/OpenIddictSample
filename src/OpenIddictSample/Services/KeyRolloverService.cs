using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace OpenIddictSample.Services;

public class KeyRolloverService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KeyRolloverService> _logger;
    private readonly TimeSpan _rolloverInterval = TimeSpan.FromDays(90); // Roll keys every 90 days

    public KeyRolloverService(
        IServiceProvider serviceProvider,
        ILogger<KeyRolloverService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Key rollover service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformKeyRolloverCheckAsync(stoppingToken);
                
                // Check every 24 hours
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during key rollover check");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task PerformKeyRolloverCheckAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        // In a production environment, you would:
        // 1. Check the age of current signing keys
        // 2. Generate new keys if needed
        // 3. Update JWKS endpoint
        // 4. Keep old keys for a grace period to validate existing tokens

        _logger.LogInformation("Performing key rollover check at {Time}", DateTime.UtcNow);

        // Simulated key rollover logic
        // In production, integrate with your key storage (Azure Key Vault, HSM, etc.)
        
        var rsa = RSA.Create(2048);
        var securityKey = new RsaSecurityKey(rsa)
        {
            KeyId = Guid.NewGuid().ToString()
        };

        _logger.LogInformation("Generated new signing key with ID: {KeyId}", securityKey.KeyId);

        // Note: OpenIddict handles key rotation automatically with its built-in key manager
        // This service demonstrates the concept. In production:
        // - OpenIddict stores signing keys in the database
        // - Multiple keys can be active simultaneously
        // - JWKS endpoint automatically includes all active keys
        // - Old keys are retained for token validation during transition period

        await Task.CompletedTask;
    }
}
