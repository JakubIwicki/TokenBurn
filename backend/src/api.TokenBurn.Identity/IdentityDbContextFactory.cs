using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OpenIddict.EntityFrameworkCore.Models;

namespace Api.TokenBurn.Identity;

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Identity")
            ?? "Host=localhost;Database=tokenburn_identity;Username=postgres";

        DbContextOptionsBuilder<IdentityDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName));
        options.UseOpenIddict();
        return new IdentityDbContext(options.Options);
    }
}
