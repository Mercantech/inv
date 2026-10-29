namespace Inv.Web.Models;

public sealed record LoanHistoryEntry(Guid Id, Guid BorrowerSubject, string? BorrowerName,
    DateTimeOffset LoanedAt, DateTimeOffset? ReturnedAt, Guid ApprovedBySubject, string? ApprovedByName,
    Guid? ReturnedBySubject, string? ReturnedByName, string? Note, string? ReturnNote);
public sealed record LoanHistoryResponse(IReadOnlyList<LoanHistoryEntry> Loans, bool DirectoryUnavailable);

public sealed record ActiveLoanResponse(Guid Id, Guid EquipmentId, int EquipmentShortId,
    string EquipmentName, string SerialNumber, Guid BorrowerSubject, string? BorrowerName,
    string? BorrowerEmail, DateTimeOffset LoanedAt);
public sealed record ActiveLoansResponse(IReadOnlyList<ActiveLoanResponse> Loans, bool DirectoryUnavailable);

public sealed record BorrowerResponse(Guid Subject, string Name, string? Email, bool IsDisabled);
public sealed record LoanResponse(Guid Id, Guid EquipmentId, Guid BorrowerSubject,
    Guid ApprovedBySubject, DateTimeOffset LoanedAt, string? Note);

public sealed record LoanRequestResponse(Guid Id, Guid EquipmentId, string EquipmentName, Guid BorrowerSubject,
    string Status, string? Note, DateTimeOffset CreatedAt);
