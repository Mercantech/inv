using Inv.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inv.Infrastructure.Data.Configurations;

internal sealed class HardwareDeviceConfiguration : IEntityTypeConfiguration<HardwareDevice>
{
    public void Configure(EntityTypeBuilder<HardwareDevice> builder)
    {
        builder.Property(item => item.ShortId).HasDefaultValueSql("nextval('\"ShortIds\"')").ValueGeneratedOnAdd();
        builder.HasIndex(item => item.ShortId).IsUnique();
        builder.HasKey(device => device.SamdId);
        builder.Property(device => device.SamdId).HasMaxLength(64).ValueGeneratedNever();
        builder.Property(device => device.Mac).HasMaxLength(32);
        builder.Property(device => device.EccSerial).HasMaxLength(64);
        builder.Property(device => device.Label).HasMaxLength(128);
        builder.HasOne(device => device.Equipment).WithOne()
            .HasForeignKey<HardwareDevice>(device => device.EquipmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
