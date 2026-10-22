using Inv.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inv.Infrastructure.Data.Configurations;

internal sealed class EquipmentCategoryConfiguration : IEntityTypeConfiguration<EquipmentCategory>
{
    public void Configure(EntityTypeBuilder<EquipmentCategory> builder)
    {
        builder.ConfigureEntity();
        builder.ToTable("EquipmentCategories", table =>
            table.HasCheckConstraint("CK_EquipmentCategories_Name", "length(btrim(\"Name\")) > 0"));
        builder.Property(category => category.Name).HasMaxLength(150).IsRequired();
        builder.Property(category => category.NormalizedName)
            .HasComputedColumnSql("lower(btrim(\"Name\"))", stored: true);
        builder.HasIndex(category => category.NormalizedName).IsUnique();
    }
}
