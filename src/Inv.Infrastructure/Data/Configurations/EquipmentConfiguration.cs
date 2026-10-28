using Inv.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inv.Infrastructure.Data.Configurations;

internal sealed class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> builder)
    {
        builder.Property(item => item.ShortId).HasDefaultValueSql("nextval('\"ShortIds\"')").ValueGeneratedOnAdd();
        builder.HasIndex(item => item.ShortId).IsUnique();
        builder.ConfigureEntity();
        builder.ToTable("Equipment", table =>
        {
            table.HasCheckConstraint("CK_Equipment_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Equipment_SerialNumber", "length(btrim(\"SerialNumber\")) > 0");
        });
        builder.Property(equipment => equipment.Name).HasMaxLength(200).IsRequired();
        builder.Property(equipment => equipment.SerialNumber).HasMaxLength(200).IsRequired();
        builder.HasOne(equipment => equipment.Category).WithMany(category => category.Equipment)
            .HasForeignKey(equipment => equipment.EquipmentCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(equipment => equipment.Name);
        builder.HasIndex(equipment => equipment.SerialNumber);
    }
}
