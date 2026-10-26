namespace Inv.Web.Models;

public sealed record BorrowerResponse(Guid Subject, string Name, string? Email, bool IsDisabled);
public sealed record LoanResponse(Guid Id, Guid EquipmentId, Guid BorrowerSubject,
    Guid ApprovedBySubject, DateTimeOffset LoanedAt, string? Note);
