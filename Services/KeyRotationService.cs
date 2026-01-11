using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenIddictSample2.Services;

/// <summary>
/// Service to manage signing key rotation and JWKS
/// </summary>
public interface IKeyRotationService
{
    Task<SigningCredentials> GetCurrentSigningKeyAsync(string? tenantId = null);
    Task<IEnumerable<SecurityKey>> GetValidationKeysAsync(string? tenantId = null);
    Task RotateKeysAsync(string? tenantId = null);
}

public class KeyRotationService : IKeyRotationService
{
    private readonly IDistributedCache _cache;
    private const string KeyPrefix = "signing_key:";
    private const string KeysListPrefix = "validation_keys:";
    private const string DefaultTenant = "default";
    private readonly TimeSpan _keyLifetime = TimeSpan.FromDays(90); // Keys valid for 90 days

    public KeyRotationService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<SigningCredentials> GetCurrentSigningKeyAsync(string? tenantId = null)
    {
        var keyId = $"{KeyPrefix}current:{tenantId ?? DefaultTenant}";
        var keyData = await _cache.GetStringAsync(keyId);

        if (string.IsNullOrEmpty(keyData))
        {
            // Generate new key if none exists
            return await GenerateAndStoreNewKeyAsync(tenantId);
        }

        var keyInfo = JsonSerializer.Deserialize<StoredKeyInfo>(keyData);
        if (keyInfo == null || keyInfo.ExpiresAt < DateTime.UtcNow)
        {
            // Key expired, generate new one
            return await GenerateAndStoreNewKeyAsync(tenantId);
        }

        var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(Convert.FromBase64String(keyInfo.PrivateKey), out _);

        var securityKey = new RsaSecurityKey(rsa) { KeyId = keyInfo.KeyId };
        return new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
    }

    public async Task<IEnumerable<SecurityKey>> GetValidationKeysAsync(string? tenantId = null)
    {
        var keysListId = $"{KeysListPrefix}{tenantId ?? DefaultTenant}";
        var keysJson = await _cache.GetStringAsync(keysListId);

        if (string.IsNullOrEmpty(keysJson))
        {
            var currentKey = await GetCurrentSigningKeyAsync(tenantId);
            return new[] { currentKey.Key };
        }

        var keyInfos = JsonSerializer.Deserialize<List<StoredKeyInfo>>(keysJson) ?? new List<StoredKeyInfo>();
        var validKeys = new List<SecurityKey>();

        foreach (var keyInfo in keyInfos)
        {
            // Include keys that haven't expired yet (grace period)
            if (keyInfo.ExpiresAt.AddDays(30) > DateTime.UtcNow)
            {
                var rsa = RSA.Create();
                rsa.ImportRSAPrivateKey(Convert.FromBase64String(keyInfo.PrivateKey), out _);
                validKeys.Add(new RsaSecurityKey(rsa) { KeyId = keyInfo.KeyId });
            }
        }

        return validKeys;
    }

    public async Task RotateKeysAsync(string? tenantId = null)
    {
        await GenerateAndStoreNewKeyAsync(tenantId);
    }

    private async Task<SigningCredentials> GenerateAndStoreNewKeyAsync(string? tenantId = null)
    {
        using var rsa = RSA.Create(2048);
        var keyId = Guid.NewGuid().ToString();

        var privateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
        var publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());

        var keyInfo = new StoredKeyInfo
        {
            KeyId = keyId,
            PrivateKey = privateKey,
            PublicKey = publicKey,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_keyLifetime),
            TenantId = tenantId
        };

        // Store current key
        var keyIdString = $"{KeyPrefix}current:{tenantId ?? DefaultTenant}";
        await _cache.SetStringAsync(keyIdString, JsonSerializer.Serialize(keyInfo),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _keyLifetime.Add(TimeSpan.FromDays(30))
            });

        // Add to validation keys list
        await AddToValidationKeysListAsync(keyInfo, tenantId);

        var securityKey = new RsaSecurityKey(rsa) { KeyId = keyId };
        return new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
    }

    private async Task AddToValidationKeysListAsync(StoredKeyInfo newKey, string? tenantId)
    {
        var keysListId = $"{KeysListPrefix}{tenantId ?? DefaultTenant}";
        var keysJson = await _cache.GetStringAsync(keysListId);

        var keyInfos = string.IsNullOrEmpty(keysJson)
            ? new List<StoredKeyInfo>()
            : JsonSerializer.Deserialize<List<StoredKeyInfo>>(keysJson) ?? new List<StoredKeyInfo>();

        keyInfos.Add(newKey);

        // Remove expired keys (older than 30 days past expiration)
        keyInfos = keyInfos
            .Where(k => k.ExpiresAt.AddDays(30) > DateTime.UtcNow)
            .ToList();

        await _cache.SetStringAsync(keysListId, JsonSerializer.Serialize(keyInfos),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(120)
            });
    }

    private sealed class StoredKeyInfo
    {
        public string KeyId { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string? TenantId { get; set; }
    }
}
