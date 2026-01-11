using StackExchange.Redis;
using System.Text.Json;

namespace OpenIddictSample.Services;

public class RedisTokenService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisTokenService> _logger;

    public RedisTokenService(IConnectionMultiplexer redis, ILogger<RedisTokenService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task StoreTokenAsync(string key, object value, TimeSpan expiration)
    {
        try
        {
            var db = _redis.GetDatabase();
            var serialized = JsonSerializer.Serialize(value);
            await db.StringSetAsync(key, serialized, expiration);
            _logger.LogInformation("Stored token in Redis with key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing token in Redis");
            throw;
        }
    }

    public async Task<T?> GetTokenAsync<T>(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);
            
            if (value.IsNullOrEmpty)
                return default;

            return JsonSerializer.Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving token from Redis");
            return default;
        }
    }

    public async Task<bool> DeleteTokenAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            var result = await db.KeyDeleteAsync(key);
            _logger.LogInformation("Deleted token from Redis with key: {Key}, Result: {Result}", key, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting token from Redis");
            return false;
        }
    }

    public async Task<bool> TokenExistsAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking token existence in Redis");
            return false;
        }
    }
}
