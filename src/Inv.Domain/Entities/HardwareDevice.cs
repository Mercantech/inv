namespace Inv.Domain.Entities;

public sealed class HardwareDevice
{
    public int ShortId { get; private set; }
    public required string SamdId { get; set; }
    public string? Mac { get; set; }
    public string? EccSerial { get; set; }
    public string? Label { get; set; }
    public Guid? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<HardwareTestReport> Reports { get; set; } = [];
}
