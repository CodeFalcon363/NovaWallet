using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NovaWallet.Core.Data;

/// <summary>Used only by `dotnet ef migrations add` at design time — never at runtime.</summary>
public class NovaWalletDbContextFactory : IDesignTimeDbContextFactory<NovaWalletDbContext>
{
    public NovaWalletDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NovaWalletDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost;Database=NovaWallet;Trusted_Connection=True;TrustServerCertificate=True;");
        return new NovaWalletDbContext(optionsBuilder.Options);
    }
}
