namespace Inv.Web.Models;

public sealed record ActiveLoanResponse(Guid Id, Guid EquipmentId, int EquipmentShortId,
    string EquipmentName, string SerialNumber, Guid BorrowerSubject, string? BorrowerName,
    string? BorrowerEmail, DateTimeOffset LoanedAt);
public sealed record ActiveLoansResponse(IReadOnlyList<ActiveLoanResponse> Loans, bool DirectoryUnavailable);

public sealed record BorrowerResponse(Guid Subject, string Name, string? Email, bool IsDisabled);
public sealed record LoanResponse(Guid Id, Guid EquipmentId, Guid BorrowerSubject,
    Guid ApprovedBySubject, DateTimeOffset LoanedAt, string? Note);
