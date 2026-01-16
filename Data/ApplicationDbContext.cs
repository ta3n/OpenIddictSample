using Microsoft.EntityFrameworkCore;
using OpenIddictSample2.Models;

namespace OpenIddictSample2.Data;

/// <summary>
/// Main database context with OpenIddict integration
/// </summary>
public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options
) : DbContext(options)
{
    public DbSet<ApplicationUser> Users { get; set; }
    public DbSet<Tenant> Tenants { get; set; }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);

        // Configure OpenIddict entities
        modelBuilder.UseOpenIddict();

        // User configuration
        modelBuilder.Entity<ApplicationUser>(
            entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.TenantId);
            }
        );

        // Tenant configuration
        modelBuilder.Entity<Tenant>(
            entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Domain).IsUnique();
            }
        );
    }
}