using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Inv.Api.Features.EquipmentCategories;

public static class EquipmentCategoryEndpoints
{
    public static IEndpointRouteBuilder MapEquipmentCategoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/equipment-categories").WithTags("EquipmentCategories")
            .RequireAuthorization();
        
        // Indloggede brugere kan hente kategorier til valg og filtrering.
        group.MapGet("/", async (InventoryDbContext Database, CancellationToken CancellationToken) =>
        {
            var categories = await Database.EquipmentCategories
                .AsNoTracking()
                .OrderBy(category => category.Name)
                .Select(category => new EquipmentCategoryResponse(
                    category.Id,
                    category.Name))
                .ToListAsync(CancellationToken);
            return Results.Ok(categories);
        });

        // Kun udstyrsansvarlige må oprette kategorier.
        group.MapPost("/", CreateAsync).RequireAuthorization("EquipmentManager");
        return endpoints;
    }

    public static async Task<IResult> CreateAsync(CreateEquipmentCategoryRequest request, InventoryDbContext database,
        CancellationToken CancellationToken)
    {
        var name = request.Name?.Trim();

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

        var category = new EquipmentCategory
        {
            Name = name
        };

        database.EquipmentCategories.Add(category);

        try
        {
            await database.SaveChangesAsync(CancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_EquipmentCategories_NormalizedName"
            })
        {
            // Databasen afviser også dubletter ved samtidige oprettelser.
            return Results.Conflict(new
            {
                Message = "Der findes allerede en kategori med det navn."
            });
        }

        return Results.Created($"/api/equipment-categories/{category.Id}",
            new EquipmentCategoryResponse(category.Id, category.Name));
    }
}