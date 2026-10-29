using Inv.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Inv.Infrastructure.Data.Configurations;
internal sealed class LoanRequestConfiguration : IEntityTypeConfiguration<LoanRequest>
{
    public void Configure(EntityTypeBuilder<LoanRequest> builder)
    {
        builder.ConfigureEntity();
        builder.Property(r => r.Note).HasMaxLength(4000);
        builder.Property(r => r.Status).HasMaxLength(16);
        builder.HasOne(r => r.Equipment).WithMany().HasForeignKey(r => r.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.EquipmentId, r.BorrowerSubject }).IsUnique().HasFilter("\"Status\" = 'Pending'");
    }
}
