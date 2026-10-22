namespace Inv.Domain.Entities;

public sealed class IssueCategory : Entity
{
    public required string Name { get; set; }
    public string NormalizedName { get; private set; } = string.Empty;
    public Guid? EquipmentCategoryId { get; set; }
    public EquipmentCategory? EquipmentCategory { get; set; }
    public ICollection<EquipmentIssue> Issues { get; set; } = [];
}
