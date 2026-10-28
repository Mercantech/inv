namespace Inv.Domain.Entities;

public sealed class Equipment : Entity
{
    public int ShortId { get; private set; }
    public required string Name { get; set; }
    public required string SerialNumber { get; set; }
    public Guid EquipmentCategoryId { get; set; }
    public EquipmentCategory Category { get; set; } = null!;
    public ICollection<Loan> Loans { get; set; } = [];
    public ICollection<EquipmentIssue> Issues { get; set; } = [];
}
