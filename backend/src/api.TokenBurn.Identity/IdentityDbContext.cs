using Api.TokenBurn.Identity.Domain;
using Api.TokenBurn.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Api.TokenBurn.Identity;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<IdentityUser> Users => Set<IdentityUser>();

    public static string[] TableNames => ["identity_users", "openiddict_applications", "openiddict_authorizations", "openiddict_scopes", "openiddict_tokens"]; // schema ownership is established by OpenIddict

    public DbSet<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication> Applications => Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication>();
    public DbSet<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreAuthorization> Authorizations => Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreAuthorization>();
    public DbSet<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreScope> Scopes => Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreScope>();
    public DbSet<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken> Tokens => Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>();



    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.Entity<IdentityUser>(entity =>
        {
            entity.ToTable(Api.TokenBurn.Identity.Persistence.TableNames.IdentityUsers);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(256).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => x.Username).IsUnique();
        });
    }
}
