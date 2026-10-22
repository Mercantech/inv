using Inv.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inv.Infrastructure.Data.Configurations;

internal sealed class EquipmentIssueConfiguration : IEntityTypeConfiguration<EquipmentIssue>
{
    public void Configure(EntityTypeBuilder<EquipmentIssue> builder)
    {
        builder.ConfigureEntity();
        builder.ToTable("EquipmentIssues", table =>
        {
            table.HasCheckConstraint("CK_EquipmentIssues_Description", "length(btrim(\"Description\")) > 0");
            table.HasCheckConstraint("CK_EquipmentIssues_Status", "\"Status\" IN ('Open', 'InProgress', 'Resolved')");
            table.HasCheckConstraint("CK_EquipmentIssues_Resolution", "(\"Status\" = 'Resolved' AND \"ResolvedAt\" IS NOT NULL AND \"ResolvedBySubject\" IS NOT NULL) OR (\"Status\" <> 'Resolved' AND \"ResolvedAt\" IS NULL AND \"ResolvedBySubject\" IS NULL)");
            table.HasCheckConstraint("CK_EquipmentIssues_ResolvedAt", "\"ResolvedAt\" IS NULL OR \"ResolvedAt\" >= \"ReportedAt\"");
            table.HasCheckConstraint("CK_EquipmentIssues_ReportedBySubject", "\"ReportedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_EquipmentIssues_ResolvedBySubject", "\"ResolvedBySubject\" IS NULL OR \"ResolvedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        builder.Property(issue => issue.Description).HasMaxLength(4000).IsRequired();
        builder.Property(issue => issue.ResolutionNote).HasMaxLength(4000);
        builder.Property(issue => issue.Status).HasConversion<string>().HasMaxLength(30);
        builder.HasOne(issue => issue.Equipment).WithMany(equipment => equipment.Issues)
            .HasForeignKey(issue => issue.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(issue => issue.Category).WithMany(category => category.Issues)
            .HasForeignKey(issue => issue.IssueCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(issue => new { issue.EquipmentId, issue.Status });
        builder.HasIndex(issue => issue.Status);
    }
}
