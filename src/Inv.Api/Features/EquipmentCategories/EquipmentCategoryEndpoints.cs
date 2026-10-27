using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inv.Api.Features.EquipmentCategories;

public static class EquipmentCategoryEndpoints
{
    public static IEndpointRouteBuilder MapEquipmentCategoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/equipment-categories").WithTags("EquipmentCategories")
            .RequireAuthorization();

        group.MapGet("/", async (InventoryDbContext database, CancellationToken cancellationToken) =>
        {
            var categories = await database.EquipmentCategories
                .AsNoTracking()
                .OrderBy(category => category.Name)
                .Select(category => new EquipmentCategoryResponse(category.Id, category.Name))
                .ToListAsync(cancellationToken);
            return Results.Ok(categories);
        });

        group.MapPost("/", CreateAsync).RequireAuthorization("EquipmentManager");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization("EquipmentManager");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("EquipmentManager");

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateEquipmentCategoryRequest request,
        InventoryDbContext database,
        CancellationToken cancellationToken)
    {
        var name = ValidateName(request.Name);
        if (name is IResult error) return error;

        var category = new EquipmentCategory { Name = (string)name };
        database.EquipmentCategories.Add(category);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueName(exception))
        {
            return Results.Conflict(new { Message = "Der findes allerede en kategori med det navn." });
        }

        return Results.Created($"/api/equipment-categories/{category.Id}",
            new EquipmentCategoryResponse(category.Id, category.Name));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        CreateEquipmentCategoryRequest request,
        InventoryDbContext database,
        CancellationToken cancellationToken)
    {
        var name = ValidateName(request.Name);
        if (name is IResult error) return error;

        var category = await database.EquipmentCategories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (category is null)
            return Results.NotFound(new { Message = "Kategorien blev ikke fundet." });

        category.Name = (string)name;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueName(exception))
        {
            return Results.Conflict(new { Message = "Der findes allerede en kategori med det navn." });
        }

        return Results.Ok(new EquipmentCategoryResponse(category.Id, category.Name));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        InventoryDbContext database,
        CancellationToken cancellationToken)
    {
        var category = await database.EquipmentCategories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (category is null)
            return Results.NotFound(new { Message = "Kategorien blev ikke fundet." });

        if (await database.Equipment.AnyAsync(item => item.EquipmentCategoryId == id, cancellationToken))
            return Results.Conflict(new { Message = "Kategorien kan ikke slettes, mens der stadig er udstyr i den." });

        if (await database.IssueCategories.AnyAsync(item => item.EquipmentCategoryId == id, cancellationToken))
            return Results.Conflict(new { Message = "Kategorien kan ikke slettes, mens der stadig er fejlkategorier knyttet til den." });

        database.EquipmentCategories.Remove(category);
        await database.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static object ValidateName(string? value)
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

    private static bool IsUniqueName(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_EquipmentCategories_NormalizedName"
        };
}
