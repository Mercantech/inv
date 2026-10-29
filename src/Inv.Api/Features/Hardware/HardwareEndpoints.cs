using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inv.Api.Features.Hardware;

public sealed record UpdateDeviceRequest(string? Label, string? Mac, string? EccSerial);

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

        group.MapPut("/devices/{samdId}", UpdateDeviceAsync).RequireAuthorization("EquipmentManager");
        group.MapDelete("/devices/{samdId}", DeleteDeviceAsync).RequireAuthorization("EquipmentManager");
        group.MapDelete("/devices/{samdId}/tests/{reportId:guid}", DeleteTestAsync).RequireAuthorization("EquipmentManager");

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
        }).RequireAuthorization("EquipmentManager");

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
                SerialNumber = samd, Category = category, EquipmentCategoryId = category.Id
            };
            db.Equipment.Add(equipment);
            device.EquipmentId = equipment.Id;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new { device.EquipmentId });
        }).RequireAuthorization("EquipmentManager");

        group.MapPost("/import", (List<LegacyDevice> devices, HardwareStore store, CancellationToken ct) => store.ImportAsync(devices, ct))
            .WithMetadata(new RequestSizeLimitAttribute(10 * 1024 * 1024))
            .RequireAuthorization("EquipmentManager");

        // Hardwarelab: alle indloggede må registrere board og indsende tests.
        group.MapPost("/devices", async (DeviceRegistration request, HardwareStore store, InventoryDbContext db, CancellationToken ct) =>
        {
            await using var transaction = await store.BeginWriteAsync(ct);
            var device = await store.RegisterAsync(request, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new { samd_id = device.SamdId, short_id = device.ShortId });
        });
        group.MapPost("/devices/{samdId}/tests", async (string samdId, TestSubmission request, HardwareStore store, InventoryDbContext db, CancellationToken ct) =>
        {
            var samd = HardwareContracts.NormalizeSamd(samdId);
            HardwareContracts.ValidateSubmission(request);
            HardwareContracts.EnsureReportSamdMatches(samd, request.Report);
            await using var transaction = await store.BeginWriteAsync(ct);
            var device = await store.EnsureRegisteredFromReportAsync(samd, request.Report, ct);
            await store.EnsureEquipmentAsync(device, ct);
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
            HardwareContracts.EnsureReportSamdMatches(samd, request.Report);
            await using var transaction = await store.BeginWriteAsync(ct);
            var device = await store.EnsureRegisteredFromReportAsync(samd, request.Report, ct);
            await store.EnsureEquipmentAsync(device, ct);
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

    private static async Task<IResult> UpdateDeviceAsync(
        string samdId,
        UpdateDeviceRequest request,
        HardwareStore store,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var samd = HardwareContracts.NormalizeSamd(samdId);
        await using var transaction = await store.BeginWriteAsync(ct);
        var device = await db.HardwareDevices.FindAsync([samd], ct);
        if (device is null) return Results.NotFound();

        if (request.Label is not null)
        {
            var label = request.Label.Trim();
            if (label.Length > 128)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Label"] = ["Navnet må højst indeholde 128 tegn."] });
            device.Label = string.IsNullOrWhiteSpace(label) ? null : label;
        }

        if (request.Mac is not null)
        {
            var mac = request.Mac.Trim();
            if (mac.Length > 32)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Mac"] = ["MAC-adressen er for lang."] });
            device.Mac = string.IsNullOrWhiteSpace(mac) ? null : mac;
        }

        if (request.EccSerial is not null)
        {
            var ecc = request.EccSerial.Trim();
            if (ecc.Length > 64)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["EccSerial"] = ["ECC-serienummeret er for langt."] });
            device.EccSerial = string.IsNullOrWhiteSpace(ecc) || ecc == "-" ? null : ecc;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteDeviceAsync(
        string samdId,
        HardwareStore store,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var samd = HardwareContracts.NormalizeSamd(samdId);
        await using var transaction = await store.BeginWriteAsync(ct);
        var device = await db.HardwareDevices.FindAsync([samd], ct);
        if (device is null) return Results.NotFound();

        var reports = await db.HardwareTestReports.Where(report => report.SamdId == samd).ToListAsync(ct);
        db.HardwareTestReports.RemoveRange(reports);
        db.HardwareDevices.Remove(device);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteTestAsync(
        string samdId,
        Guid reportId,
        HardwareStore store,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var samd = HardwareContracts.NormalizeSamd(samdId);
        await using var transaction = await store.BeginWriteAsync(ct);
        var report = await db.HardwareTestReports.SingleOrDefaultAsync(
            item => item.Id == reportId && item.SamdId == samd, ct);
        if (report is null) return Results.NotFound();
        db.HardwareTestReports.Remove(report);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
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
