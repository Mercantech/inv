using Inv.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inv.Infrastructure.Data.Configurations;

internal sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ConfigureEntity();
        builder.ToTable("Loans", table =>
        {
            table.HasCheckConstraint("CK_Loans_ReturnedAt", "\"ReturnedAt\" IS NULL OR \"ReturnedAt\" >= \"LoanedAt\"");
            table.HasCheckConstraint("CK_Loans_ReturnApproval", "(\"ReturnedAt\" IS NULL) = (\"ReturnedBySubject\" IS NULL)");
            table.HasCheckConstraint("CK_Loans_BorrowerSubject", "\"BorrowerSubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Loans_ApprovedBySubject", "\"ApprovedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Loans_ReturnedBySubject", "\"ReturnedBySubject\" IS NULL OR \"ReturnedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        builder.Property(loan => loan.Note).HasMaxLength(4000);
        builder.Property(loan => loan.ReturnNote).HasMaxLength(4000);
        builder.HasOne(loan => loan.Equipment).WithMany(equipment => equipment.Loans)
            .HasForeignKey(loan => loan.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(loan => loan.EquipmentId).IsUnique()
            .HasFilter("\"ReturnedAt\" IS NULL").HasDatabaseName("UX_Loans_ActiveEquipment");
        builder.HasIndex(loan => new { loan.EquipmentId, loan.LoanedAt });
        builder.HasIndex(loan => loan.BorrowerSubject);
    }
}
