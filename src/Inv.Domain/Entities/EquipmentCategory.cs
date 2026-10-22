namespace Inv.Domain.Entities;

public sealed class EquipmentCategory : Entity
{
    public required string Name { get; set; }
    public string NormalizedName { get; private set; } = string.Empty;
    public ICollection<Equipment> Equipment { get; set; } = [];
    public ICollection<IssueCategory> IssueCategories { get; set; } = [];
}
