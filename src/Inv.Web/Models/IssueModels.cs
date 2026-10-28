namespace Inv.Web.Models;

public sealed record IssueCategoryResponse(Guid Id, string Name, Guid? EquipmentCategoryId);

public sealed class IssueCategoryFormModel
{
    public string Name { get; set; } = "";
    public Guid? EquipmentCategoryId { get; set; }
}

public sealed record EquipmentIssueResponse(
    Guid Id,
    Guid EquipmentId,
    Guid IssueCategoryId,
    string CategoryName,
    string Description,
    Guid ReportedBySubject,
    DateTimeOffset ReportedAt,
    string Status,
    string? ResolutionNote,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedBySubject);

public sealed class CreateIssueFormModel
{
    public Guid? IssueCategoryId { get; set; }
    public string Description { get; set; } = "";
}

public sealed class UpdateIssueFormModel
{
    public Guid? IssueCategoryId { get; set; }
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Open";
    public string? ResolutionNote { get; set; }
}

public sealed record FaultCategoryResponse(string Key, string Name, string Source);
public sealed record HardwareFaultResponse(string Key, string Name, Guid EquipmentId, string SamdId,
    Guid ReportId, DateTimeOffset TestedAt, string? Note);
