using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inv.Api.Features.Hardware;

public static class HardwareEndpoints
{
    public static IEndpointRouteBuilder MapHardwareEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/hardware").WithTags("Hardware").RequireAuthorization();
        group.AddEndpointFilter<HardwareValidationFilter>();
        group.MapGet("/devices", (Guid? equipmentId, HardwareStore store, CancellationToken ct) => store.ListAsync(equipmentId, ct));
        group.MapGet("/devices/{samdId}/tests", async (string samdId, int? skip, int? take, InventoryDbContext db, CancellationToken ct) =>
        {
            var samd = HardwareContracts.NormalizeSamd(samdId);
            if ((skip ?? 0) < 0 || (take ?? 20) is < 1 or > 100)
                return Results.BadRequest(new { Message = "Vælg 1–100 rapporter og et positivt startpunkt." });
            if (!await db.HardwareDevices.AnyAsync(device => device.SamdId == samd, ct)) return Results.NotFound();
            var reports = await db.HardwareTestReports.AsNoTracking().Where(report => report.SamdId == samd)
                .OrderByDescending(report => report.TestedAt).ThenByDescending(report => report.Id)
                .Skip(skip ?? 0).Take(take ?? 20).ToListAsync(ct);
            return Results.Ok(reports.Select(HardwareContracts.Result));
        });
        group.MapPost("/devices/{samdId}/equipment", async (string samdId, LinkEquipmentRequest request, HardwareStore store,
            InventoryDbContext db, CancellationToken ct) =>
        {
            var samd = HardwareContracts.NormalizeSamd(samdId);
            await using var transaction = await store.BeginWriteAsync(ct);
            var device = await db.HardwareDevices.FindAsync([samd], ct);
            if (device is null) return Results.NotFound();
            if (request.EquipmentId.HasValue)
            {
                if (!await db.Equipment.AnyAsync(item => item.Id == request.EquipmentId, ct))
                    return Results.NotFound(new { Message = "Udstyret findes ikke." });
                if (await db.HardwareDevices.AnyAsync(other => other.EquipmentId == request.EquipmentId && other.SamdId != samd, ct))
                    return Results.Conflict(new { Message = "Udstyret er allerede knyttet til et andet board." });
            }
            device.EquipmentId = request.EquipmentId;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        });
        group.MapPost("/devices/{samdId}/create-equipment", async (string samdId, HardwareStore store, InventoryDbContext db, CancellationToken ct) =>
        {
            var samd = HardwareContracts.NormalizeSamd(samdId);
            await using var transaction = await store.BeginWriteAsync(ct);
            var device = await db.HardwareDevices.FindAsync([samd], ct);
            if (device is null) return Results.NotFound();
            if (device.EquipmentId.HasValue) return Results.Ok(new { device.EquipmentId });
            if (await db.Equipment.AnyAsync(item => item.SerialNumber.ToUpper() == samd, ct))
                return Results.Conflict(new { Message = "Der findes allerede udstyr med dette serienummer. Vælg det eksisterende udstyr." });
            var category = await db.EquipmentCategories.FirstOrDefaultAsync(item => item.Name.ToLower() == "arduino oplà", ct);
            if (category is null)
            {
                category = new EquipmentCategory { Name = "Arduino Oplà" };
                db.EquipmentCategories.Add(category);
            }
            var equipment = new Inv.Domain.Entities.Equipment
            {
                Name = string.IsNullOrWhiteSpace(device.Label) ? "Arduino Oplà" : device.Label,
                SerialNumber = samd, Category = category, EquipmentCategoryId = category.Id,
                Condition = EquipmentCondition.Unknown
            };
            db.Equipment.Add(equipment);
            device.EquipmentId = equipment.Id;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new { device.EquipmentId });
        });
        group.MapPost("/import", (List<LegacyDevice> devices, HardwareStore store, CancellationToken ct) => store.ImportAsync(devices, ct))
            .WithMetadata(new RequestSizeLimitAttribute(10 * 1024 * 1024));

        // Open ingestion for Oplà boards; human access still uses Mercantec Auth.
        var ingestion = endpoints.MapGroup("/api/devices").WithTags("Hardware ingestion").AllowAnonymous()
            .RequireRateLimiting("hardware-ingestion").WithMetadata(new RequestSizeLimitAttribute(65536));
        ingestion.AddEndpointFilter<HardwareValidationFilter>();
        ingestion.MapPost("", async (DeviceRegistration request, HardwareStore store, InventoryDbContext db, CancellationToken ct) =>
        {
            await using var transaction = await store.BeginWriteAsync(ct);
            var device = await store.RegisterAsync(request, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new { samd_id = device.SamdId, short_id = device.ShortId });
        });
        ingestion.MapPost("/{samdId}/tests", async (string samdId, TestSubmission request, HardwareStore store, InventoryDbContext db, CancellationToken ct) =>
        {
            var samd = HardwareContracts.NormalizeSamd(samdId);
            HardwareContracts.ValidateSubmission(request);
            if (request.Report is { } payload && payload.TryGetProperty("samd_id", out var bodySamd)
                && (bodySamd.ValueKind != System.Text.Json.JsonValueKind.String
                    || HardwareContracts.NormalizeSamd(bodySamd.GetString()) != samd))
                throw new ArgumentException("Rapportens SAMD-ID svarer ikke til boardet i adressen.");
            await using var transaction = await store.BeginWriteAsync(ct);
            var device = await db.HardwareDevices.FindAsync([samd], ct);
            if (device is null) return Results.NotFound(new { Message = "Registrér boardet før testen sendes." });
            var report = new HardwareTestReport
            {
                Device = device, SamdId = samd, Passed = request.Passed!.Value,
                PassedCount = request.PassedCount!.Value, FailedCount = request.FailedCount!.Value,
                ReportJson = request.Report!.Value.GetRawText(), Details = request.Details
            };
            device.LastSeenAt = report.TestedAt;
            db.HardwareTestReports.Add(report);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/hardware/devices/{samd}/tests", HardwareContracts.Result(report));
        });
        return endpoints;
    }
}

internal sealed class HardwareValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["Hardware"] = [exception.Message] });
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            return Results.Conflict(new { Message = "Boardet, kategorien eller udstyret er blevet ændret. Genindlæs og prøv igen." });
        }
    }
}
