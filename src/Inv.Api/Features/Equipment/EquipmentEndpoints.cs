// src/Inv.Api/Features/Equipment/EquipmentEndpoints.cs

using Inv.Domain.Entities;
using Inv.Api.Features.Issues;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using EquipmentEntity = Inv.Domain.Entities.Equipment;

namespace Inv.Api.Features.Equipment;

public static class EquipmentEndpoints
{
    public static IEndpointRouteBuilder MapEquipmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/equipment")
            .WithTags("Equipment")
            .RequireAuthorization();

        group.MapGet("/", async (
            string? search,
            Guid? categoryId,
            EquipmentCondition? condition,
            bool? isLoaned,
            string? fault,
            InventoryDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (condition.HasValue && !Enum.IsDefined(condition.Value))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Condition"] = ["Vælg en gyldig tilstand."]
                });
            }

            var query = database.Equipment.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim()
                    .Replace("\\", "\\\\")
                    .Replace("%", "\\%")
                    .Replace("_", "\\_");

                var pattern = $"%{term}%";

                query = query.Where(item =>
                    item.ShortId.ToString() == search.Trim().TrimStart('#') ||
                    EF.Functions.ILike(item.Name, pattern, "\\") ||
                    EF.Functions.ILike(item.SerialNumber, pattern, "\\"));
            }

            if (categoryId.HasValue)
            {
                query = query.Where(item =>
                    item.EquipmentCategoryId == categoryId.Value);
            }

            if (condition.HasValue)
            {
                query = query.Where(item =>
                    item.Condition == condition.Value);
            }

            if (isLoaned.HasValue)
            {
                query = query.Where(item => item.Loans.Any(loan => loan.ReturnedAt == null) == isLoaned.Value);
            }

            if (!string.IsNullOrWhiteSpace(fault))
            {
                if (fault.StartsWith("manual:") && Guid.TryParse(fault[7..], out var issueCategoryId))
                    query = query.Where(e => database.EquipmentIssues.Any(i => i.EquipmentId == e.Id
                        && i.IssueCategoryId == issueCategoryId && i.Status != EquipmentIssueStatus.Resolved));
                else if (fault.StartsWith("hardware:") && fault.Length > 9 && fault.Length <= 100)
                {
                    var ids = (await HardwareFaults.CurrentAsync(database, null, cancellationToken))
                        .Where(f => f.Key == fault).Select(f => f.EquipmentId).Distinct().ToList();
                    query = query.Where(e => ids.Contains(e.Id));
                }
                else return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Fault"] = ["Vælg en gyldig fejlkategori."]
                });
            }

            var equipment = await query
                .OrderBy(item => item.Name)
                .ThenBy(item => item.Id)
                .Select(item => new EquipmentResponse(
                    item.Id,
                    item.Name,
                    item.SerialNumber,
                    item.EquipmentCategoryId,
                    item.Category.Name,
                    item.Condition,
                    item.CreatedAt,
                    item.UpdatedAt, item.ShortId, item.Loans.Any(loan => loan.ReturnedAt == null)))
                .ToListAsync(cancellationToken);

            return Results.Ok(equipment);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            InventoryDbContext database,
            CancellationToken cancellationToken) =>
        {
            var equipment = await database.Equipment
                .AsNoTracking()
                .Where(item => item.Id == id)
                .Select(item => new EquipmentResponse(
                    item.Id,
                    item.Name,
                    item.SerialNumber,
                    item.EquipmentCategoryId,
                    item.Category.Name,
                    item.Condition,
                    item.CreatedAt,
                    item.UpdatedAt, item.ShortId, item.Loans.Any(loan => loan.ReturnedAt == null)))
                .SingleOrDefaultAsync(cancellationToken);

            return equipment is null
                ? Results.NotFound(new { Message = "Udstyret blev ikke fundet." })
                : Results.Ok(equipment);
        });

        group.MapPost("/", CreateAsync).RequireAuthorization("EquipmentManager");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization("EquipmentManager");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("EquipmentManager");

        return endpoints;
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        InventoryDbContext database,
        CancellationToken cancellationToken)
    {
        var equipment = await database.Equipment.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (equipment is null)
            return Results.NotFound(new { Message = "Udstyret blev ikke fundet." });

        if (await database.Loans.AnyAsync(loan => loan.EquipmentId == id && loan.ReturnedAt == null, cancellationToken))
            return Results.Conflict(new { Message = "Udstyret kan ikke slettes, mens det er udlånt. Registrér returnering først." });

        if (await database.EquipmentIssues.AnyAsync(issue => issue.EquipmentId == id, cancellationToken))
            return Results.Conflict(new { Message = "Udstyret kan ikke slettes, mens der findes fejlrapporter. Slet eller flyt fejlene først." });

        var linked = await database.HardwareDevices.Where(device => device.EquipmentId == id).ToListAsync(cancellationToken);
        foreach (var device in linked)
            device.EquipmentId = null;

        database.Equipment.Remove(equipment);
        await database.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateAsync(
        SaveEquipmentRequest request,
        InventoryDbContext database,
        CancellationToken cancellationToken)
        => await SaveAsync(null, request, database, cancellationToken);

    private static async Task<IResult> UpdateAsync(
        Guid id,
        SaveEquipmentRequest request,
        InventoryDbContext database,
        CancellationToken cancellationToken)
        => await SaveAsync(id, request, database, cancellationToken);

    private static async Task<IResult> SaveAsync(
        Guid? id,
        SaveEquipmentRequest request,
        InventoryDbContext database,
        CancellationToken cancellationToken)
    {
        var equipment = id.HasValue
            ? await database.Equipment.SingleOrDefaultAsync(item => item.Id == id.Value, cancellationToken)
            : null;

        if (id.HasValue && equipment is null)
        {
            return Results.NotFound(new { Message = "Udstyret blev ikke fundet." });
        }

        var name = request.Name?.Trim();
        var serialNumber = request.SerialNumber?.Trim();
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["Name"] = ["Navnet må ikke være tomt."];
        }
        else if (name.Length > 200)
        {
            errors["Name"] = ["Navnet må højst indeholde 200 tegn."];
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            errors["SerialNumber"] = ["Serienummeret må ikke være tomt."];
        }
        else if (serialNumber.Length > 200)
        {
            errors["SerialNumber"] = ["Serienummeret må højst indeholde 200 tegn."];
        }

        if (request.EquipmentCategoryId is null ||
            request.EquipmentCategoryId == Guid.Empty)
        {
            errors["EquipmentCategoryId"] = ["Vælg en kategori."];
        }

        if (request.Condition is null ||
            !Enum.IsDefined(request.Condition.Value))
        {
            errors["Condition"] = ["Vælg en gyldig tilstand."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var category = await database.EquipmentCategories
            .SingleOrDefaultAsync(
                item => item.Id == request.EquipmentCategoryId!.Value,
                cancellationToken);

        if (category is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["EquipmentCategoryId"] = ["Den valgte kategori findes ikke."]
            });
        }

        if (equipment is null)
        {
            equipment = new EquipmentEntity { Name = name!, SerialNumber = serialNumber! };
            database.Equipment.Add(equipment);
        }

        equipment.Name = name!;
        equipment.SerialNumber = serialNumber!;
        equipment.EquipmentCategoryId = category.Id;
        equipment.Category = category;
        equipment.Condition = request.Condition!.Value;

        // Også en gemning uden feltændringer opdaterer ændringstidspunktet.
        if (id.HasValue)
        {
            database.Entry(equipment).Property(item => item.Name).IsModified = true;
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.ForeignKeyViolation
            })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["EquipmentCategoryId"] =
                    ["Kategorien findes ikke længere. Vælg en anden kategori."]
            });
        }

        var response = new EquipmentResponse(
                equipment.Id,
                equipment.Name,
                equipment.SerialNumber,
                equipment.EquipmentCategoryId,
                category.Name,
                equipment.Condition,
                equipment.CreatedAt,
                equipment.UpdatedAt, equipment.ShortId,
                await database.Loans.AnyAsync(loan => loan.EquipmentId == equipment.Id && loan.ReturnedAt == null, cancellationToken));

        return id.HasValue
            ? Results.Ok(response)
            : Results.Created($"/api/equipment/{equipment.Id}", response);
    }
}
