using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Inv.Infrastructure.Configuration;

namespace Inv.Infrastructure.Data;

public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        LocalDevelopmentEnvironment.Load();
        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Sæt CONNECTION_STRING eller ConnectionStrings__Default, før EF-værktøjet køres.");
        }

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new InventoryDbContext(options);
    }
}
