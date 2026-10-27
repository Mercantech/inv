using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inv.Api.Features.Hardware;

public sealed class HardwareStore(InventoryDbContext database)
{
    // Ingestion, linking and imports share a lock, including across API replicas.
    // This keeps simultaneous first registrations/imports from creating duplicate rows.
    internal async Task<IDbContextTransaction> BeginWriteAsync(CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72641026)", cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    internal async Task<HardwareDevice> RegisterAsync(DeviceRegistration request, CancellationToken cancellationToken)
    {
        HardwareContracts.ValidateRegistration(request);
        var samd = HardwareContracts.NormalizeSamd(request.SamdId);
        var device = await database.HardwareDevices.FindAsync([samd], cancellationToken);
        if (device is null)
        {
            device = new HardwareDevice { SamdId = samd };
            database.HardwareDevices.Add(device);
        }
        device.Mac = request.Mac ?? device.Mac;
        device.EccSerial = request.EccSerial ?? device.EccSerial;
        device.Label = request.Label ?? device.Label;
        device.LastSeenAt = DateTimeOffset.UtcNow;
        return device;
    }

    /// <summary>
    /// Finder board på SAMD-ID, eller opretter det ud fra rapportens ids (mac/ecc/label).
    /// </summary>
    internal Task<HardwareDevice> EnsureRegisteredFromReportAsync(
        string samd,
        System.Text.Json.JsonElement? report,
        CancellationToken cancellationToken)
    {
        string? mac = null, ecc = null, label = null;
        if (report is { ValueKind: System.Text.Json.JsonValueKind.Object } root)
        {
            if (root.TryGetProperty("mac", out var macEl) && macEl.ValueKind == System.Text.Json.JsonValueKind.String)
                mac = macEl.GetString();
            if (root.TryGetProperty("ecc_serial", out var eccEl) && eccEl.ValueKind == System.Text.Json.JsonValueKind.String)
                ecc = eccEl.GetString();
            if (root.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == System.Text.Json.JsonValueKind.String)
                label = labelEl.GetString();
        }

        return RegisterAsync(new DeviceRegistration(samd, mac, string.IsNullOrWhiteSpace(ecc) || ecc == "-" ? null : ecc,
            string.IsNullOrWhiteSpace(label) ? "Arduino Oplà" : label), cancellationToken);
    }

    public async Task<ImportResult> ImportAsync(List<LegacyDevice> devices, CancellationToken cancellationToken)
    {
        if (devices.Count > 10000) throw new ArgumentException("Importen indeholder for mange boards.");
        var seen = new HashSet<long>();
        foreach (var device in devices)
        {
            if (device is null) throw new ArgumentException("Importen indeholder et tomt board.");
            HardwareContracts.ValidateRegistration(new(device.SamdId, device.Mac, device.EccSerial, device.Label));
            if (device.CreatedAt == default || device.UpdatedAt == default || device.Tests is null)
                throw new ArgumentException("Importen mangler registreringstidspunkt eller testhistorik.");
            foreach (var test in device.Tests)
                if (test is null || test.Id <= 0 || !seen.Add(test.Id) || test.CreatedAt == default
                    || HardwareContracts.NormalizeSamd(test.SamdId) != HardwareContracts.NormalizeSamd(device.SamdId)
                    || test.PassedCount < 0 || test.FailedCount < 0)
                    throw new ArgumentException("Importen har ugyldigt test-ID, tidspunkt eller tilknytning til board.");
        }

        await using var transaction = await BeginWriteAsync(cancellationToken);
        var existing = (await database.HardwareTestReports
            .Where(report => report.ImportSource == "arduino-opla" && report.LegacyReportId != null)
            .Select(report => report.LegacyReportId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var addedDevices = 0;
        var addedReports = 0;
        var skipped = 0;
        foreach (var input in devices)
        {
            var samd = HardwareContracts.NormalizeSamd(input.SamdId);
            var device = await database.HardwareDevices.FindAsync([samd], cancellationToken);
            if (device is null)
            {
                device = new HardwareDevice
                {
                    SamdId = samd, Mac = input.Mac, EccSerial = input.EccSerial, Label = input.Label,
                    RegisteredAt = input.CreatedAt.ToUniversalTime(), LastSeenAt = input.UpdatedAt.ToUniversalTime()
                };
                database.HardwareDevices.Add(device);
                addedDevices++;
            }
            foreach (var test in input.Tests)
            {
                if (!existing.Add(test.Id)) { skipped++; continue; }
                var parsed = test.Report is { ValueKind: System.Text.Json.JsonValueKind.Object } report
                    ? report : HardwareContracts.ParseReport(null, test.Details);
                database.HardwareTestReports.Add(new HardwareTestReport
                {
                    SamdId = samd, Device = device, Passed = test.Passed, PassedCount = test.PassedCount,
                    FailedCount = test.FailedCount, TestedAt = test.CreatedAt.ToUniversalTime(),
                    ReportJson = parsed?.GetRawText(), Details = test.Details,
                    ImportSource = "arduino-opla", LegacyReportId = test.Id
                });
                if (test.CreatedAt > device.LastSeenAt) device.LastSeenAt = test.CreatedAt.ToUniversalTime();
                addedReports++;
            }
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImportResult(addedDevices, addedReports, skipped);
    }

    internal async Task<List<DeviceResult>> ListAsync(Guid? equipmentId, CancellationToken cancellationToken)
    {
        var query = database.HardwareDevices.AsNoTracking();
        if (equipmentId.HasValue) query = query.Where(device => device.EquipmentId == equipmentId);
        var rows = await query.OrderByDescending(device => device.LastSeenAt).ThenBy(device => device.SamdId)
            .Select(device => new
            {
                device.ShortId, EquipmentShortId = device.Equipment == null ? (int?)null : device.Equipment.ShortId,
                device.SamdId, device.Label, device.Mac, device.EccSerial, device.EquipmentId,
                EquipmentName = device.Equipment == null ? null : device.Equipment.Name,
                device.LastSeenAt, TestCount = device.Reports.Count,
                Latest = device.Reports.OrderByDescending(report => report.TestedAt).ThenByDescending(report => report.Id).FirstOrDefault()
            }).ToListAsync(cancellationToken);
        return rows.Select(row => new DeviceResult(row.SamdId, row.Label, row.Mac, row.EccSerial,
            row.EquipmentId, row.EquipmentName, row.LastSeenAt, row.TestCount,
            row.Latest is null ? null : HardwareContracts.Result(row.Latest), row.ShortId, row.EquipmentShortId)).ToList();
    }
}
