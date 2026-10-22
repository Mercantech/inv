using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Inv.Infrastructure.Data.Migrations;

[DbContext(typeof(InventoryDbContext))]
public class InventoryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => InitialInventoryModel.Build(modelBuilder);
}
