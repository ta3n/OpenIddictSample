using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace OpenIddictSample2.Services;

/// <summary>
/// BFF Session Service - manages user sessions and tokens for BFF pattern
/// Stores tokens securely on server-side instead of exposing to frontend
/// </summary>
public interface IBffSessionService
{
    Task<BffSession?> GetSessionAsync(
        string sessionId
    );

    Task CreateSessionAsync(
        string sessionId,
        BffSession session
    );

    Task UpdateSessionAsync(
        string sessionId,
        BffSession session
    );

    Task DeleteSessionAsync(
        string sessionId
    );

    Task<bool> RefreshSessionTokensAsync(
        string sessionId
    );
}

public class BffSession
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;

    // Tokens stored server-side only
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public string? IdToken { get; set; }

    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
}

public class BffSessionService(
    IDistributedCache cache,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration
)
    : IBffSessionService
{
    private const string SessionPrefix = "bff_session:";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    public async Task<BffSession?> GetSessionAsync(
        string sessionId
    )
    {
        var key = SessionPrefix + sessionId;
        var json = await cache.GetStringAsync(key);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var session = JsonSerializer.Deserialize<BffSession>(json);

        if (session != null)
        {
            // Update last accessed time
            session.LastAccessedAt = DateTime.UtcNow;
            await UpdateSessionAsync(sessionId, session);
        }

        return session;
    }

    public async Task CreateSessionAsync(
        string sessionId,
        BffSession session
    )
    {
        var key = SessionPrefix + sessionId;
        var json = JsonSerializer.Serialize(session);

        await cache.SetStringAsync(
            key,
            json,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = SessionLifetime
            }
        );
    }

    public async Task UpdateSessionAsync(
        string sessionId,
        BffSession session
    )
    {
        await CreateSessionAsync(sessionId, session);
    }

    public async Task DeleteSessionAsync(
        string sessionId
    )
    {
        var key = SessionPrefix + sessionId;
        await cache.RemoveAsync(key);
    }

    public async Task<bool> RefreshSessionTokensAsync(
        string sessionId
    )
    {
        var session = await GetSessionAsync(sessionId);
        if (session == null || string.IsNullOrEmpty(session.RefreshToken))
        {
            return false;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            var tokenEndpoint = configuration["OpenIddict:TokenEndpoint"] ?? "https://localhost:5001/connect/token";

            var requestContent = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", session.RefreshToken),
                    new KeyValuePair<string, string>("client_id", configuration["OpenIddict:ClientId"] ?? "postman-client"),
                    new KeyValuePair<string, string>("client_secret", configuration["OpenIddict:ClientSecret"] ?? "postman-secret")
                ]
            );

            var response = await client.PostAsync(tokenEndpoint, requestContent);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content);

            if (tokenResponse == null)
            {
                return false;
            }

            // Update session with new tokens
            session.AccessToken = tokenResponse.AccessToken ?? string.Empty;
            session.RefreshToken = tokenResponse.RefreshToken ?? session.RefreshToken;
            session.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            await UpdateSessionAsync(sessionId, session);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private class TokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
    }
}