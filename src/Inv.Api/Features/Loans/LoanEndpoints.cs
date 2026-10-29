using System.Security.Claims;
using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inv.Api.Features.Loans;

public sealed record CreateLoanRequest(Guid? BorrowerSubject, string? Note);
public sealed record ReturnLoanRequest(string? ReturnNote, Guid? LoanId = null);
public sealed record LoanResponse(Guid Id, Guid EquipmentId, Guid BorrowerSubject,
    Guid ApprovedBySubject, DateTimeOffset LoanedAt, string? Note);
public sealed record ActiveLoanResponse(Guid Id, Guid EquipmentId, int EquipmentShortId,
    string EquipmentName, string SerialNumber, Guid BorrowerSubject, string? BorrowerName,
    string? BorrowerEmail, DateTimeOffset LoanedAt);
public sealed record ActiveLoansResponse(IReadOnlyList<ActiveLoanResponse> Loans, bool DirectoryUnavailable);
public sealed record LoanHistoryEntry(Guid Id, Guid BorrowerSubject, string? BorrowerName,
    DateTimeOffset LoanedAt, DateTimeOffset? ReturnedAt, Guid ApprovedBySubject, string? ApprovedByName,
    Guid? ReturnedBySubject, string? ReturnedByName, string? Note, string? ReturnNote);
public sealed record LoanHistoryResponse(IReadOnlyList<LoanHistoryEntry> Loans, bool DirectoryUnavailable);

public sealed record SubmitLoanRequest(string? Note);
public sealed record ReviewLoanRequest(bool Approve);
public sealed record LoanRequestResponse(Guid Id, Guid EquipmentId, string EquipmentName, Guid BorrowerSubject,
    string Status, string? Note, DateTimeOffset CreatedAt);

public static class LoanEndpoints
{
    public static IEndpointRouteBuilder MapLoanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").RequireAuthorization().WithTags("Loans");

        group.MapGet("/loan-request-access", () => Results.Ok()).RequireAuthorization("EquipmentManager");
        group.MapGet("/loans", GetActiveAsync);
        group.MapGet("/equipment/{id:guid}/loans", GetHistoryAsync);
        group.MapGet("/loan-access", () => Results.Ok(new { Allowed = true })).RequireAuthorization("LoanManager");

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
        }).RequireAuthorization("LoanManager");

        group.MapGet("/equipment/{id:guid}/loan", async (Guid id, InventoryDbContext db, CancellationToken ct) =>
        {
            if (!await db.Equipment.AnyAsync(e => e.Id == id, ct)) return Results.NotFound();
            var loan = await db.Loans.AsNoTracking().Where(l => l.EquipmentId == id && l.ReturnedAt == null)
                .Select(l => new LoanResponse(l.Id, l.EquipmentId, l.BorrowerSubject, l.ApprovedBySubject, l.LoanedAt, l.Note))
                .SingleOrDefaultAsync(ct);
            return loan is null ? Results.NoContent() : Results.Ok(loan);
        });

        group.MapPost("/equipment/{id:guid}/loans", CreateAsync).RequireAuthorization("LoanManager");
        group.MapPost("/equipment/{id:guid}/loans/return", ReturnAsync).RequireAuthorization("LoanManager");
        group.MapGet("/management/loan-requests", async (InventoryDbContext db, CancellationToken ct) =>
            Results.Ok(await db.LoanRequests.AsNoTracking().OrderByDescending(r => r.CreatedAt)
                .Select(r => new LoanRequestResponse(r.Id, r.EquipmentId, r.Equipment.Name,
                    r.BorrowerSubject, r.Status, r.Note, r.CreatedAt)).ToListAsync(ct)))
            .RequireAuthorization("EquipmentManager");
        group.MapGet("/loan-requests", async (ClaimsPrincipal user, InventoryDbContext db,
            Microsoft.AspNetCore.Authorization.IAuthorizationService authorization, Guid? equipmentId, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var subject) || subject == Guid.Empty) return Results.Unauthorized();
            var manager = (await authorization.AuthorizeAsync(user, null, "EquipmentManager")).Succeeded;
            var query = db.LoanRequests.AsNoTracking();
            if (!manager) query = query.Where(r => r.BorrowerSubject == subject);
            if (equipmentId.HasValue) query = query.Where(r => r.EquipmentId == equipmentId);
            return Results.Ok(await query.OrderByDescending(r => r.CreatedAt).Select(r => new LoanRequestResponse(
                r.Id, r.EquipmentId, r.Equipment.Name, r.BorrowerSubject, r.Status, r.Note, r.CreatedAt)).ToListAsync(ct));
        });
        group.MapPost("/equipment/{id:guid}/loan-requests", async (Guid id, SubmitLoanRequest request,
            ClaimsPrincipal user, InventoryDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var subject) || subject == Guid.Empty) return Results.Unauthorized();
            if (request.Note?.Trim().Length > 4000) return Results.BadRequest(new { Message = "Noten må højst være 4000 tegn." });
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72641027)", ct);
            if (!await db.Equipment.AnyAsync(e => e.Id == id, ct)) return Results.NotFound();
            if (await db.Loans.AnyAsync(l => l.EquipmentId == id && l.ReturnedAt == null, ct)) return AlreadyLoaned();
            if (await db.LoanRequests.AnyAsync(r => r.EquipmentId == id && r.BorrowerSubject == subject && r.Status == "Pending", ct))
                return Results.Conflict(new { Message = "Du har allerede en afventende anmodning." });
            var row = new LoanRequest { EquipmentId = id, BorrowerSubject = subject, Note = request.Note?.Trim() };
            db.LoanRequests.Add(row);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/loan-requests", new { row.Id });
        });
        group.MapPost("/loan-requests/{requestId:guid}/review", async (Guid requestId, ReviewLoanRequest review,
            ClaimsPrincipal user, InventoryDbContext db, IBorrowerDirectory directory, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var subject) || subject == Guid.Empty) return Results.Unauthorized();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72641027)", ct);
            var row = await db.LoanRequests.SingleOrDefaultAsync(r => r.Id == requestId, ct);
            if (row is null) return Results.NotFound();
            if (row.Status != "Pending") return Results.Conflict(new { Message = "Anmodningen er allerede behandlet." });
            if (review.Approve)
            {
                var result = await CreateAsync(row.EquipmentId, new CreateLoanRequest(row.BorrowerSubject, row.Note), user, db, directory, ct);
                if (result is not Microsoft.AspNetCore.Http.HttpResults.Created<LoanResponse>) return result;
            }
            row.Status = review.Approve ? "Approved" : "Rejected";
            row.ReviewedBySubject = subject;
            row.ReviewedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization("EquipmentManager");
        return endpoints;
    }

    private static IResult DirectoryUnavailable() => Results.Problem(statusCode: 503,
        detail: "Lånerregistret kunne ikke hentes fra Mercantec Auth. Prøv igen senere, eller kontakt administratoren.");

    private static async Task<IResult> GetHistoryAsync(Guid id, InventoryDbContext db,
        IBorrowerDirectory directory, CancellationToken ct)
    {
        if (!await db.Equipment.AnyAsync(e => e.Id == id, ct)) return Results.NotFound();
        var loans = await db.Loans.AsNoTracking().Where(l => l.EquipmentId == id)
            .OrderByDescending(l => l.LoanedAt).ThenByDescending(l => l.Id)
            .Select(l => new LoanHistoryEntry(l.Id, l.BorrowerSubject, null, l.LoanedAt, l.ReturnedAt,
                l.ApprovedBySubject, null, l.ReturnedBySubject, null, l.Note, l.ReturnNote)).ToListAsync(ct);
        var unavailable = false;
        if (loans.Count > 0)
        {
            try
            {
                var people = (await directory.GetAsync(ct)).ToDictionary(b => b.Subject);
                string? Name(Guid? subject) => subject.HasValue && people.TryGetValue(subject.Value, out var person) ? person.Name : null;
                loans = loans.Select(l => l with { BorrowerName = Name(l.BorrowerSubject),
                    ApprovedByName = Name(l.ApprovedBySubject), ReturnedByName = Name(l.ReturnedBySubject) }).ToList();
            }
            catch (DirectoryUnavailableException) { unavailable = true; }
        }
        return Results.Ok(new LoanHistoryResponse(loans, unavailable));
    }

    private static async Task<IResult> GetActiveAsync(string? search, InventoryDbContext db,
        IBorrowerDirectory directory, CancellationToken ct)
    {
        var loans = await db.Loans.AsNoTracking().Where(l => l.ReturnedAt == null)
            .OrderByDescending(l => l.LoanedAt).ThenBy(l => l.Id)
            .Select(l => new ActiveLoanResponse(l.Id, l.EquipmentId, l.Equipment.ShortId,
                l.Equipment.Name, l.Equipment.SerialNumber, l.BorrowerSubject, null, null, l.LoanedAt))
            .ToListAsync(ct);
        var unavailable = false;
        if (loans.Count > 0)
        {
            try
            {
                var borrowers = (await directory.GetAsync(ct)).ToDictionary(b => b.Subject);
                loans = loans.Select(l => borrowers.TryGetValue(l.BorrowerSubject, out var borrower)
                    ? l with { BorrowerName = borrower.Name, BorrowerEmail = borrower.Email } : l).ToList();
            }
            catch (DirectoryUnavailableException) { unavailable = true; }
        }
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            loans = loans.Where(l => l.EquipmentName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || l.SerialNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || l.EquipmentShortId.ToString().Equals(term.TrimStart('#'), StringComparison.OrdinalIgnoreCase)
                || (l.BorrowerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (l.BorrowerEmail?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || l.BorrowerSubject.ToString().Equals(term, StringComparison.OrdinalIgnoreCase)).ToList();
        return Results.Ok(new ActiveLoansResponse(loans, unavailable));
    }

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

    private static async Task<IResult> ReturnAsync(
        Guid id,
        ReturnLoanRequest request,
        ClaimsPrincipal user,
        InventoryDbContext db,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty)
            return Results.Unauthorized();

        var note = request.ReturnNote?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (note?.Length > 4000) errors["ReturnNote"] = ["Noten må højst indeholde 4000 tegn."];
        if (request.LoanId is null || request.LoanId == Guid.Empty) errors["LoanId"] = ["Angiv det aktive udlån."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (!await db.Equipment.AnyAsync(e => e.Id == id, ct))
            return Results.NotFound(new { Message = "Udstyret blev ikke fundet." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Lås det konkrete udlån: samtidige kald må hverken overskrive returgodkender
        // eller returnere et nyt udlån fra en gammel browserformular.
        var locked = await db.Loans.FromSqlInterpolated($"SELECT * FROM \"Loans\" WHERE \"Id\" = {request.LoanId.GetValueOrDefault()} AND \"EquipmentId\" = {id} FOR UPDATE").ToListAsync(ct);
        var loan = locked.SingleOrDefault();
        if (loan is null || loan.ReturnedAt is not null)
            return Results.Conflict(new { Message = "Udlånet er allerede afsluttet eller er ikke længere det aktuelle udlån. Opdatér siden." });

        loan.ReturnedAt = DateTimeOffset.UtcNow;
        loan.ReturnedBySubject = actor;
        loan.ReturnNote = string.IsNullOrWhiteSpace(note) ? null : note;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static IResult AlreadyLoaned() => Results.Conflict(new { Message = "Udstyret er allerede udlånt. Opdatér siden for at se det aktuelle udlån." });
}
