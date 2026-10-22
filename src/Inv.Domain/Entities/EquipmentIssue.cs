namespace Inv.Domain.Entities;

public sealed class EquipmentIssue : Entity
{
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public Guid IssueCategoryId { get; set; }
    public IssueCategory Category { get; set; } = null!;
    public required string Description { get; set; }
    public Guid ReportedBySubject { get; set; }
    public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.UtcNow;
    public EquipmentIssueStatus Status { get; set; } = EquipmentIssueStatus.Open;
    public string? ResolutionNote { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBySubject { get; set; }
}
