namespace Inv.Domain.Entities;
public sealed class LoanRequest : Entity
{
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public Guid BorrowerSubject { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? ReviewedBySubject { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
