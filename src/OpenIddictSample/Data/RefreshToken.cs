namespace OpenIddictSample.Data;

public class RefreshToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Token { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public string? ReplacedByToken { get; set; }
}
