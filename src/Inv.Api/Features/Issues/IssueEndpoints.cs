using System.Security.Claims;
using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inv.Api.Features.Issues;

public sealed record IssueCategoryResponse(Guid Id, string Name, Guid? EquipmentCategoryId);
public sealed record SaveIssueCategoryRequest(string? Name, Guid? EquipmentCategoryId);
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
public sealed record CreateIssueRequest(Guid? IssueCategoryId, string? Description);
public sealed record UpdateIssueRequest(Guid? IssueCategoryId, string? Description, string? Status, string? ResolutionNote);

public static class IssueEndpoints
{
    public static IEndpointRouteBuilder MapIssueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var categories = endpoints.MapGroup("/api/issue-categories").WithTags("IssueCategories")
            .RequireAuthorization();
        endpoints.MapGet("/api/fault-categories", async (InventoryDbContext db, CancellationToken ct) =>
            Results.Ok(await HardwareFaults.CategoriesAsync(db, ct))).RequireAuthorization();
        endpoints.MapGet("/api/equipment/{equipmentId:guid}/hardware-faults", async (
            Guid equipmentId, InventoryDbContext db, CancellationToken ct) =>
        {
            if (!await db.Equipment.AnyAsync(e => e.Id == equipmentId, ct)) return Results.NotFound();
            return Results.Ok(await HardwareFaults.CurrentAsync(db, equipmentId, ct));
        }).RequireAuthorization();
        categories.MapGet("/", ListCategoriesAsync);
        categories.MapPost("/", CreateCategoryAsync).RequireAuthorization("EquipmentManager");
        categories.MapPut("/{id:guid}", UpdateCategoryAsync).RequireAuthorization("EquipmentManager");
        categories.MapDelete("/{id:guid}", DeleteCategoryAsync).RequireAuthorization("EquipmentManager");

        var issues = endpoints.MapGroup("/api/equipment/{equipmentId:guid}/issues").WithTags("EquipmentIssues")
            .RequireAuthorization();
        issues.MapGet("/", ListIssuesAsync);
        issues.MapPost("/", CreateIssueAsync).RequireAuthorization("EquipmentManager");
        issues.MapPut("/{issueId:guid}", UpdateIssueAsync).RequireAuthorization("EquipmentManager");
        issues.MapDelete("/{issueId:guid}", DeleteIssueAsync).RequireAuthorization("EquipmentManager");

        return endpoints;
    }

    private static async Task<IResult> ListCategoriesAsync(InventoryDbContext db, CancellationToken ct)
    {
        var rows = await db.IssueCategories.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new IssueCategoryResponse(item.Id, item.Name, item.EquipmentCategoryId))
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateCategoryAsync(
        SaveIssueCategoryRequest request,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var name = ValidateCategoryName(request.Name);
        if (name is IResult error) return error;

        if (request.EquipmentCategoryId is Guid equipmentCategoryId)
        {
            if (!await db.EquipmentCategories.AnyAsync(item => item.Id == equipmentCategoryId, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["EquipmentCategoryId"] = ["Udstyrskategorien findes ikke."]
                });
            }
        }

        var category = new IssueCategory
        {
            Name = (string)name,
            EquipmentCategoryId = request.EquipmentCategoryId
        };
        db.IssueCategories.Add(category);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (IsUniqueCategory(exception))
        {
            return Results.Conflict(new { Message = "Der findes allerede en fejlkategori med det navn." });
        }

        return Results.Created($"/api/issue-categories/{category.Id}",
            new IssueCategoryResponse(category.Id, category.Name, category.EquipmentCategoryId));
    }

    private static async Task<IResult> UpdateCategoryAsync(
        Guid id,
        SaveIssueCategoryRequest request,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var name = ValidateCategoryName(request.Name);
        if (name is IResult error) return error;

        var category = await db.IssueCategories.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (category is null)
            return Results.NotFound(new { Message = "Fejlkategorien blev ikke fundet." });

        if (request.EquipmentCategoryId is Guid equipmentCategoryId)
        {
            if (!await db.EquipmentCategories.AnyAsync(item => item.Id == equipmentCategoryId, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["EquipmentCategoryId"] = ["Udstyrskategorien findes ikke."]
                });
            }
        }

        category.Name = (string)name;
        category.EquipmentCategoryId = request.EquipmentCategoryId;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (IsUniqueCategory(exception))
        {
            return Results.Conflict(new { Message = "Der findes allerede en fejlkategori med det navn." });
        }

        return Results.Ok(new IssueCategoryResponse(category.Id, category.Name, category.EquipmentCategoryId));
    }

    private static async Task<IResult> DeleteCategoryAsync(Guid id, InventoryDbContext db, CancellationToken ct)
    {
        var category = await db.IssueCategories.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (category is null)
            return Results.NotFound(new { Message = "Fejlkategorien blev ikke fundet." });

        if (await db.EquipmentIssues.AnyAsync(issue => issue.IssueCategoryId == id, ct))
            return Results.Conflict(new { Message = "Fejlkategorien kan ikke slettes, mens den bruges af fejlrapporter." });

        db.IssueCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListIssuesAsync(Guid equipmentId, InventoryDbContext db, CancellationToken ct)
    {
        if (!await db.Equipment.AnyAsync(item => item.Id == equipmentId, ct))
            return Results.NotFound(new { Message = "Udstyret blev ikke fundet." });

        var rows = await db.EquipmentIssues.AsNoTracking()
            .Where(issue => issue.EquipmentId == equipmentId)
            .OrderByDescending(issue => issue.ReportedAt)
            .Select(issue => new EquipmentIssueResponse(
                issue.Id,
                issue.EquipmentId,
                issue.IssueCategoryId,
                issue.Category.Name,
                issue.Description,
                issue.ReportedBySubject,
                issue.ReportedAt,
                issue.Status.ToString(),
                issue.ResolutionNote,
                issue.ResolvedAt,
                issue.ResolvedBySubject))
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateIssueAsync(
        Guid equipmentId,
        CreateIssueRequest request,
        ClaimsPrincipal user,
        InventoryDbContext db,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var reporter) || reporter == Guid.Empty)
            return Results.Unauthorized();

        if (!await db.Equipment.AnyAsync(item => item.Id == equipmentId, ct))
            return Results.NotFound(new { Message = "Udstyret blev ikke fundet." });

        var errors = new Dictionary<string, string[]>();
        var description = request.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
            errors["Description"] = ["Beskrivelsen må ikke være tom."];
        else if (description.Length > 4000)
            errors["Description"] = ["Beskrivelsen må højst indeholde 4000 tegn."];

        if (request.IssueCategoryId is null || request.IssueCategoryId == Guid.Empty)
            errors["IssueCategoryId"] = ["Vælg en fejlkategori."];
        else if (!await db.IssueCategories.AnyAsync(item => item.Id == request.IssueCategoryId, ct))
            errors["IssueCategoryId"] = ["Fejlkategorien findes ikke."];

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var issue = new EquipmentIssue
        {
            EquipmentId = equipmentId,
            IssueCategoryId = request.IssueCategoryId!.Value,
            Description = description!,
            ReportedBySubject = reporter,
            ReportedAt = DateTimeOffset.UtcNow,
            Status = EquipmentIssueStatus.Open
        };
        db.EquipmentIssues.Add(issue);
        await db.SaveChangesAsync(ct);

        var categoryName = await db.IssueCategories.AsNoTracking()
            .Where(item => item.Id == issue.IssueCategoryId)
            .Select(item => item.Name)
            .SingleAsync(ct);

        return Results.Created($"/api/equipment/{equipmentId}/issues/{issue.Id}",
            new EquipmentIssueResponse(
                issue.Id, issue.EquipmentId, issue.IssueCategoryId, categoryName, issue.Description,
                issue.ReportedBySubject, issue.ReportedAt, issue.Status.ToString(),
                issue.ResolutionNote, issue.ResolvedAt, issue.ResolvedBySubject));
    }

    private static async Task<IResult> UpdateIssueAsync(
        Guid equipmentId,
        Guid issueId,
        UpdateIssueRequest request,
        ClaimsPrincipal user,
        InventoryDbContext db,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty)
            return Results.Unauthorized();

        var issue = await db.EquipmentIssues
            .Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == issueId && item.EquipmentId == equipmentId, ct);
        if (issue is null)
            return Results.NotFound(new { Message = "Fejlrapporten blev ikke fundet." });

        var errors = new Dictionary<string, string[]>();
        if (request.Description is not null)
        {
            var description = request.Description.Trim();
            if (string.IsNullOrWhiteSpace(description))
                errors["Description"] = ["Beskrivelsen må ikke være tom."];
            else if (description.Length > 4000)
                errors["Description"] = ["Beskrivelsen må højst indeholde 4000 tegn."];
            else
                issue.Description = description;
        }

        if (request.IssueCategoryId is Guid categoryId)
        {
            if (!await db.IssueCategories.AnyAsync(item => item.Id == categoryId, ct))
                errors["IssueCategoryId"] = ["Fejlkategorien findes ikke."];
            else
                issue.IssueCategoryId = categoryId;
        }

        EquipmentIssueStatus? nextStatus = null;
        if (request.Status is not null)
        {
            if (!Enum.TryParse<EquipmentIssueStatus>(request.Status, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
                errors["Status"] = ["Vælg Open, InProgress eller Resolved."];
            else
                nextStatus = parsed;
        }

        var resolution = request.ResolutionNote?.Trim();
        if (resolution?.Length > 4000)
            errors["ResolutionNote"] = ["Løsningsnoten må højst indeholde 4000 tegn."];

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (nextStatus is EquipmentIssueStatus.Resolved)
        {
            issue.Status = EquipmentIssueStatus.Resolved;
            issue.ResolvedAt = DateTimeOffset.UtcNow;
            issue.ResolvedBySubject = actor;
            issue.ResolutionNote = string.IsNullOrWhiteSpace(resolution) ? issue.ResolutionNote : resolution;
            if (string.IsNullOrWhiteSpace(issue.ResolutionNote))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ResolutionNote"] = ["Angiv en løsningsnote, når fejlrapporten lukkes."]
                });
            }
        }
        else if (nextStatus is { } status)
        {
            issue.Status = status;
            issue.ResolvedAt = null;
            issue.ResolvedBySubject = null;
            if (resolution is not null)
                issue.ResolutionNote = string.IsNullOrWhiteSpace(resolution) ? null : resolution;
        }
        else if (resolution is not null)
        {
            issue.ResolutionNote = string.IsNullOrWhiteSpace(resolution) ? null : resolution;
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.CheckViolation })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Status"] = ["Status og løsning stemmer ikke overens."]
            });
        }

        await db.Entry(issue).Reference(item => item.Category).LoadAsync(ct);
        return Results.Ok(new EquipmentIssueResponse(
            issue.Id, issue.EquipmentId, issue.IssueCategoryId, issue.Category.Name, issue.Description,
            issue.ReportedBySubject, issue.ReportedAt, issue.Status.ToString(),
            issue.ResolutionNote, issue.ResolvedAt, issue.ResolvedBySubject));
    }

    private static async Task<IResult> DeleteIssueAsync(
        Guid equipmentId,
        Guid issueId,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var issue = await db.EquipmentIssues.SingleOrDefaultAsync(
            item => item.Id == issueId && item.EquipmentId == equipmentId, ct);
        if (issue is null)
            return Results.NotFound(new { Message = "Fejlrapporten blev ikke fundet." });

        db.EquipmentIssues.Remove(issue);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static object ValidateCategoryName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = ["Navnet må ikke være tomt."]
            });
        }

        if (name.Length > 150)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = ["Navnet må højst indeholde 150 tegn."]
            });
        }

        return name;
    }

    private static bool IsUniqueCategory(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
