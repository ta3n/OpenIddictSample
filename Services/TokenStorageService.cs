using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace OpenIddictSample2.Services;

/// <summary>
/// Redis-backed token storage service
/// Stores refresh tokens and handles rotation
/// </summary>
public interface ITokenStorageService
{
    Task StoreRefreshTokenAsync(string tokenId, RefreshTokenData data, TimeSpan expiration);
    Task<RefreshTokenData?> GetRefreshTokenAsync(string tokenId);
    Task RevokeRefreshTokenAsync(string tokenId);
    Task RevokeAllTokensForUserAsync(string userId);
    Task<bool> IsTokenRevokedAsync(string tokenId);
}

public class RefreshTokenData
{
    public string TokenId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? PreviousTokenId { get; set; } // For rotation chain tracking
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int RotationCount { get; set; }
}

public class TokenStorageService : ITokenStorageService
{
    private readonly IDistributedCache _cache;
    private const string TokenPrefix = "refresh_token:";
    private const string UserTokensPrefix = "user_tokens:";
    private const string RevokedPrefix = "revoked:";

    public TokenStorageService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task StoreRefreshTokenAsync(string tokenId, RefreshTokenData data, TimeSpan expiration)
    {
        var key = TokenPrefix + tokenId;
        var json = JsonSerializer.Serialize(data);

        await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        });

        // Track user's tokens for bulk revocation
        await AddTokenToUserListAsync(data.UserId, tokenId, expiration);
    }

    public async Task<RefreshTokenData?> GetRefreshTokenAsync(string tokenId)
    {
        var key = TokenPrefix + tokenId;
        var json = await _cache.GetStringAsync(key);

        if (string.IsNullOrEmpty(json))
            return null;

        return JsonSerializer.Deserialize<RefreshTokenData>(json);
    }

    public async Task RevokeRefreshTokenAsync(string tokenId)
    {
        var key = TokenPrefix + tokenId;
        await _cache.RemoveAsync(key);

        // Add to revoked list
        var revokedKey = RevokedPrefix + tokenId;
        await _cache.SetStringAsync(revokedKey, "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) // Keep revoked status
        });
    }

    public async Task RevokeAllTokensForUserAsync(string userId)
    {
        var userTokensKey = UserTokensPrefix + userId;
        var json = await _cache.GetStringAsync(userTokensKey);

        if (!string.IsNullOrEmpty(json))
        {
            var tokenIds = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

            foreach (var tokenId in tokenIds)
            {
                await RevokeRefreshTokenAsync(tokenId);
            }

            await _cache.RemoveAsync(userTokensKey);
        }
    }

    public async Task<bool> IsTokenRevokedAsync(string tokenId)
    {
        var revokedKey = RevokedPrefix + tokenId;
        var result = await _cache.GetStringAsync(revokedKey);
        return !string.IsNullOrEmpty(result);
    }

    private async Task AddTokenToUserListAsync(string userId, string tokenId, TimeSpan expiration)
    {
        var userTokensKey = UserTokensPrefix + userId;
        var json = await _cache.GetStringAsync(userTokensKey);

        var tokenIds = string.IsNullOrEmpty(json)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

        tokenIds.Add(tokenId);

        var updatedJson = JsonSerializer.Serialize(tokenIds);
        await _cache.SetStringAsync(userTokensKey, updatedJson, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        });
    }
}
