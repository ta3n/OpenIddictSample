using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace OpenIddictSample2.Services;

/// <summary>
/// Redis-backed token storage service
/// Stores refresh tokens and handles rotation
/// </summary>
public interface ITokenStorageService
{
    /// <summary>
    /// Stores a refresh token in the distributed cache with the specified expiration.
    /// </summary>
    /// <param name="tokenId">The unique identifier for the refresh token.</param>
    /// <param name="data">The refresh token data to store.</param>
    /// <param name="expiration">The time span after which the token expires.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StoreRefreshTokenAsync(
        string tokenId,
        RefreshTokenData data,
        TimeSpan expiration
    );

    /// <summary>
    /// Retrieves refresh token data from the distributed cache.
    /// </summary>
    /// <param name="tokenId">The unique identifier for the refresh token.</param>
    /// <returns>A task representing the asynchronous operation, containing the refresh token data if found; otherwise, null.</returns>
    Task<RefreshTokenData?> GetRefreshTokenAsync(
        string tokenId
    );

    /// <summary>
    /// Revokes a refresh token by removing it from storage and marking it as revoked.
    /// </summary>
    /// <param name="tokenId">The unique identifier for the refresh token to revoke.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RevokeRefreshTokenAsync(
        string tokenId
    );

    /// <summary>
    /// Revokes all refresh tokens associated with a specific user.
    /// </summary>
    /// <param name="userId">The unique identifier for the user whose tokens should be revoked.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RevokeAllTokensForUserAsync(
        string userId
    );

    /// <summary>
    /// Checks whether a refresh token has been revoked.
    /// </summary>
    /// <param name="tokenId">The unique identifier for the refresh token.</param>
    /// <returns>A task representing the asynchronous operation, containing true if the token is revoked; otherwise, false.</returns>
    Task<bool> IsTokenRevokedAsync(
        string tokenId
    );
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

public class TokenStorageService(
    IDistributedCache cache
) : ITokenStorageService
{
    private const string TokenPrefix = "refresh_token:";
    private const string UserTokensPrefix = "user_tokens:";
    private const string RevokedPrefix = "revoked:";

    public async Task StoreRefreshTokenAsync(
        string tokenId,
        RefreshTokenData data,
        TimeSpan expiration
    )
    {
        var key = TokenPrefix + tokenId;
        var json = JsonSerializer.Serialize(data);

        await cache.SetStringAsync(
            key,
            json,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration
            }
        );

        // Track user's tokens for bulk revocation
        await AddTokenToUserListAsync(data.UserId, tokenId, expiration);
    }

    public async Task<RefreshTokenData?> GetRefreshTokenAsync(
        string tokenId
    )
    {
        var key = TokenPrefix + tokenId;
        var json = await cache.GetStringAsync(key);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<RefreshTokenData>(json);
    }

    public async Task RevokeRefreshTokenAsync(
        string tokenId
    )
    {
        var key = TokenPrefix + tokenId;
        await cache.RemoveAsync(key);

        // Add to revoked list
        var revokedKey = RevokedPrefix + tokenId;
        await cache.SetStringAsync(
            revokedKey,
            "1",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) // Keep revoked status
            }
        );
    }

    public async Task RevokeAllTokensForUserAsync(
        string userId
    )
    {
        var userTokensKey = UserTokensPrefix + userId;
        var json = await cache.GetStringAsync(userTokensKey);

        if (!string.IsNullOrEmpty(json))
        {
            var tokenIds = JsonSerializer.Deserialize<List<string>>(json) ?? [];

            foreach (var tokenId in tokenIds) await RevokeRefreshTokenAsync(tokenId);

            await cache.RemoveAsync(userTokensKey);
        }
    }

    public async Task<bool> IsTokenRevokedAsync(
        string tokenId
    )
    {
        var revokedKey = RevokedPrefix + tokenId;
        var result = await cache.GetStringAsync(revokedKey);
        return !string.IsNullOrEmpty(result);
    }

    private async Task AddTokenToUserListAsync(
        string userId,
        string tokenId,
        TimeSpan expiration
    )
    {
        var userTokensKey = UserTokensPrefix + userId;
        var json = await cache.GetStringAsync(userTokensKey);

        var tokenIds = string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json) ?? [];

        tokenIds.Add(tokenId);

        var updatedJson = JsonSerializer.Serialize(tokenIds);
        await cache.SetStringAsync(
            userTokensKey,
            updatedJson,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration
            }
        );
    }
}