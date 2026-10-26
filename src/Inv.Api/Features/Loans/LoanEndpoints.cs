using System.Security.Claims;
using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inv.Api.Features.Loans;

public sealed record CreateLoanRequest(Guid? BorrowerSubject, string? Note);
public sealed record LoanResponse(Guid Id, Guid EquipmentId, Guid BorrowerSubject,
    Guid ApprovedBySubject, DateTimeOffset LoanedAt, string? Note);

public static class LoanEndpoints
{
    public static IEndpointRouteBuilder MapLoanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Samme midlertidige adgangsmodel som oprettelse/redigering af udstyr.
        var group = endpoints.MapGroup("/api").RequireAuthorization().WithTags("Loans");
        group.MapGet("/borrowers", async (string? search, IBorrowerDirectory directory, CancellationToken ct) =>
        {
            try
            {
                var borrowers = await directory.GetAsync(ct);
                var term = search?.Trim();
                return Results.Ok(borrowers.Where(b => string.IsNullOrEmpty(term)
                    || b.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (b.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || b.Subject.ToString().Equals(term, StringComparison.OrdinalIgnoreCase)));
            }
            catch (DirectoryUnavailableException) { return DirectoryUnavailable(); }
        });
        group.MapGet("/equipment/{id:guid}/loan", async (Guid id, InventoryDbContext db, CancellationToken ct) =>
        {
            if (!await db.Equipment.AnyAsync(e => e.Id == id, ct)) return Results.NotFound();
            var loan = await db.Loans.AsNoTracking().Where(l => l.EquipmentId == id && l.ReturnedAt == null)
                .Select(l => new LoanResponse(l.Id, l.EquipmentId, l.BorrowerSubject, l.ApprovedBySubject, l.LoanedAt, l.Note))
                .SingleOrDefaultAsync(ct);
            return loan is null ? Results.NoContent() : Results.Ok(loan);
        });
        group.MapPost("/equipment/{id:guid}/loans", CreateAsync);
        return endpoints;
    }

    private static IResult DirectoryUnavailable() => Results.Problem(statusCode: 503,
        detail: "Lånerregistret kunne ikke hentes fra Mercantec Auth. Prøv igen senere, eller kontakt administratoren.");

    private static async Task<IResult> CreateAsync(Guid id, CreateLoanRequest request, ClaimsPrincipal user,
        InventoryDbContext db, IBorrowerDirectory directory, CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var approver) || approver == Guid.Empty)
            return Results.Unauthorized();
        var errors = new Dictionary<string, string[]>();
        if (request.BorrowerSubject is null || request.BorrowerSubject == Guid.Empty)
            errors["BorrowerSubject"] = ["Vælg en låner fra Mercantec Auth."];
        var note = request.Note?.Trim();
        if (note?.Length > 4000) errors["Note"] = ["Noten må højst indeholde 4000 tegn."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (!await db.Equipment.AnyAsync(e => e.Id == id, ct))
            return Results.NotFound(new { Message = "Udstyret blev ikke fundet." });
        if (await db.Loans.AnyAsync(l => l.EquipmentId == id && l.ReturnedAt == null, ct))
            return AlreadyLoaned();
        IReadOnlyList<BorrowerResponse> borrowers;
        try { borrowers = await directory.GetAsync(ct); }
        catch (DirectoryUnavailableException) { return DirectoryUnavailable(); }
        var borrower = borrowers.SingleOrDefault(b => b.Subject == request.BorrowerSubject);
        if (borrower is null || borrower.IsDisabled)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["BorrowerSubject"] = ["Låneren findes ikke eller er deaktiveret i Mercantec Auth. Vælg en aktiv konto."]
            });
        var loan = new Loan
        {
            EquipmentId = id, BorrowerSubject = borrower.Subject, ApprovedBySubject = approver,
            LoanedAt = DateTimeOffset.UtcNow, Note = string.IsNullOrWhiteSpace(note) ? null : note
        };
        db.Loans.Add(loan);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_Loans_ActiveEquipment" })
        {
            // Indekset afgør også løbet mellem to samtidige oprettelser.
            return AlreadyLoaned();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return Results.NotFound(new { Message = "Udstyret findes ikke længere." });
        }
        return Results.Created($"/api/equipment/{id}/loan", new LoanResponse(loan.Id, id,
            loan.BorrowerSubject, loan.ApprovedBySubject, loan.LoanedAt, loan.Note));
    }

    private static IResult AlreadyLoaned() => Results.Conflict(new { Message = "Udstyret er allerede udlånt. Opdatér siden for at se det aktuelle udlån." });
}
