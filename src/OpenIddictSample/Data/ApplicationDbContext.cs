using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace OpenIddictSample.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure OpenIddict entities with multi-tenant support
        builder.Entity<OpenIddictEntityFrameworkCoreApplication>(entity =>
        {
            entity.HasIndex(e => e.ClientId).HasFilter("[TenantId] IS NOT NULL");
        });

        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization>(entity =>
        {
            entity.HasIndex(e => e.Subject).HasFilter("[TenantId] IS NOT NULL");
        });

        builder.Entity<OpenIddictEntityFrameworkCoreToken>(entity =>
        {
            entity.HasIndex(e => e.Subject).HasFilter("[TenantId] IS NOT NULL");
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Username }).IsUnique();
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.UserId });
        });
    }
}
