using Inv.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inv.Infrastructure.Data.Configurations;

internal sealed class HardwareTestReportConfiguration : IEntityTypeConfiguration<HardwareTestReport>
{
    public void Configure(EntityTypeBuilder<HardwareTestReport> builder)
    {
        builder.ConfigureEntity();
        builder.ToTable("HardwareTestReports", table =>
            table.HasCheckConstraint("CK_HardwareTestReports_Counts", "\"PassedCount\" >= 0 AND \"FailedCount\" >= 0"));
        builder.Property(report => report.SamdId).HasMaxLength(64);
        builder.Property(report => report.ReportJson).HasColumnType("jsonb");
        builder.Property(report => report.ImportSource).HasMaxLength(200);
        builder.HasOne(report => report.Device).WithMany(device => device.Reports)
            .HasForeignKey(report => report.SamdId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(report => new { report.SamdId, report.TestedAt });
        builder.HasIndex(report => new { report.ImportSource, report.LegacyReportId }).IsUnique();
    }
}
