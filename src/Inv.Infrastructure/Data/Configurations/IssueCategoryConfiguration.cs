using Inv.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inv.Infrastructure.Data.Configurations;

internal sealed class IssueCategoryConfiguration : IEntityTypeConfiguration<IssueCategory>
{
    public void Configure(EntityTypeBuilder<IssueCategory> builder)
    {
        builder.ConfigureEntity();
        builder.ToTable("IssueCategories", table =>
            table.HasCheckConstraint("CK_IssueCategories_Name", "length(btrim(\"Name\")) > 0"));
        builder.Property(category => category.Name).HasMaxLength(150).IsRequired();
        builder.Property(category => category.NormalizedName)
            .HasComputedColumnSql("lower(btrim(\"Name\"))", stored: true);
        builder.HasOne(category => category.EquipmentCategory).WithMany(category => category.IssueCategories)
            .HasForeignKey(category => category.EquipmentCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(category => new { category.EquipmentCategoryId, category.NormalizedName })
            .IsUnique().HasFilter("\"EquipmentCategoryId\" IS NOT NULL");
        builder.HasIndex(category => category.NormalizedName).IsUnique()
            .HasFilter("\"EquipmentCategoryId\" IS NULL").HasDatabaseName("UX_IssueCategories_GlobalName");
    }
}
