namespace OpenIddictSample2.Models;

/// <summary>
/// Tenant model for multi-tenant isolation
/// </summary>
public class Tenant
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    
    // Tenant-specific signing key identifier
    public string? SigningKeyId { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
