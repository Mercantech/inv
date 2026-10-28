using Inv.Api.Features.Hardware;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inv.Api.Features.Issues;

public sealed record FaultCategoryResponse(string Key, string Name, string Source);
public sealed record HardwareFaultResponse(string Key, string Name, Guid EquipmentId, string SamdId, Guid ReportId,
    DateTimeOffset TestedAt, string? Note);

internal static class HardwareFaults
{
    internal static string Key(string name) => "hardware:" + name.Trim().ToUpperInvariant();

    internal static async Task<List<HardwareFaultResponse>> CurrentAsync(
        InventoryDbContext db, Guid? equipmentId, CancellationToken ct)
    {
        var devices = db.HardwareDevices.AsNoTracking().Where(d => d.EquipmentId != null);
        if (equipmentId.HasValue) devices = devices.Where(d => d.EquipmentId == equipmentId);
        var rows = await devices.Select(d => new { EquipmentId = d.EquipmentId!.Value,
            Report = d.Reports.OrderByDescending(r => r.TestedAt).ThenByDescending(r => r.Id).FirstOrDefault() }).ToListAsync(ct);
        return rows.Where(row => row.Report is not null).SelectMany(row =>
        {
            var r = row.Report!;
            var result = HardwareContracts.Result(r);
            if (!result.HasComponentDetails)
                return result.Passed ? [] : new List<HardwareFaultResponse>
                {
                    new("hardware:UNKNOWN", "Ukendt hardwarefejl", row.EquipmentId, r.SamdId, r.Id, r.TestedAt,
                        "Rapporten mangler komponentdetaljer.")
                };
            return result.Components.Where(c => !c.Ok && !c.Skipped)
                .Select(c => new HardwareFaultResponse(Key(c.Name), c.Name.Trim(), row.EquipmentId, r.SamdId,
                    r.Id, r.TestedAt, c.Note)).ToList();
        }).ToList();
    }

    internal static async Task<List<FaultCategoryResponse>> CategoriesAsync(InventoryDbContext db, CancellationToken ct)
    {
        // Read only the component payloads: categories also include components currently passing.
        var reports = await db.HardwareTestReports.AsNoTracking()
            .Select(r => new { r.ReportJson, r.Details }).ToListAsync(ct);
        var categories = reports.SelectMany(r => HardwareContracts.Components(
                HardwareContracts.ParseReport(r.ReportJson, r.Details)))
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new FaultCategoryResponse(Key(c.Name), c.Name.Trim(), "Hardwaretest"))
            .DistinctBy(c => c.Key).ToList();
        categories.Add(new("hardware:UNKNOWN", "Ukendt hardwarefejl", "Hardwaretest"));
        categories.AddRange(await db.IssueCategories.AsNoTracking()
            .Select(c => new FaultCategoryResponse("manual:" + c.Id, c.Name, "Manuel")).ToListAsync(ct));
        return categories.OrderBy(c => c.Source).ThenBy(c => c.Name).ToList();
    }
}
