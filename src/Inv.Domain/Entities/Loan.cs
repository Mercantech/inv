namespace Inv.Domain.Entities;

public sealed class Loan : Entity
{
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public Guid BorrowerSubject { get; set; }
    public Guid ApprovedBySubject { get; set; }
    public DateTimeOffset LoanedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Note { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public Guid? ReturnedBySubject { get; set; }
    public string? ReturnNote { get; set; }
}
