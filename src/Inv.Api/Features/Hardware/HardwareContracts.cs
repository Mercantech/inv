using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Inv.Domain.Entities;

namespace Inv.Api.Features.Hardware;

public sealed record DeviceRegistration(
    [property: JsonPropertyName("samd_id")] string? SamdId,
    string? Mac,
    [property: JsonPropertyName("ecc_serial")] string? EccSerial,
    string? Label);

public sealed record TestSubmission(
    bool? Passed,
    [property: JsonPropertyName("passed_count")] int? PassedCount,
    [property: JsonPropertyName("failed_count")] int? FailedCount,
    JsonElement? Report,
    string? Details);

public sealed record LegacyDevice(
    [property: JsonPropertyName("samd_id")] string SamdId,
    string? Mac,
    [property: JsonPropertyName("ecc_serial")] string? EccSerial,
    string? Label,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    List<LegacyTest> Tests);

public sealed record LegacyTest(
    long Id,
    [property: JsonPropertyName("samd_id")] string SamdId,
    bool Passed,
    [property: JsonPropertyName("passed_count")] int PassedCount,
    [property: JsonPropertyName("failed_count")] int FailedCount,
    string? Details,
    JsonElement? Report,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record ComponentResult(string Name, bool Ok, bool Skipped, string? Note);
public sealed record TestResult(Guid Id, DateTimeOffset TestedAt, bool Passed, int PassedCount,
    int FailedCount, List<ComponentResult> Components, bool Imported, bool HasComponentDetails);
public sealed record DeviceResult(string SamdId, string? Label, string? Mac, string? EccSerial,
    Guid? EquipmentId, string? EquipmentName, DateTimeOffset LastSeenAt, int TestCount, TestResult? LatestTest);
public sealed record LinkEquipmentRequest(Guid? EquipmentId);
public sealed record ImportResult(int DevicesAdded, int ReportsAdded, int ReportsSkipped);

internal static partial class HardwareContracts
{
    public static string NormalizeSamd(string? value)
    {
        var samd = value?.Trim().ToUpperInvariant();
        if (samd is null || !SamdPattern().IsMatch(samd))
            throw new ArgumentException("SAMD-ID skal indeholde 8–64 hexadecimale tegn.");
        return samd;
    }

    [GeneratedRegex("^[0-9A-F]{8,64}$")]
    private static partial Regex SamdPattern();

    public static void ValidateRegistration(DeviceRegistration registration)
    {
        NormalizeSamd(registration.SamdId);
        if (registration.Mac?.Length > 32 || registration.EccSerial?.Length > 64 || registration.Label?.Length > 128)
            throw new ArgumentException("MAC, ECC-serienummer eller navn er for langt.");
    }

    public static List<ComponentResult> Components(JsonElement? report)
    {
        if (report is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("tests", out var tests) || tests.ValueKind != JsonValueKind.Array)
            return [];
        var results = new List<ComponentResult>();
        foreach (var test in tests.EnumerateArray())
        {
            if (test.ValueKind != JsonValueKind.Object
                || !test.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !test.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                continue;
            var skipped = test.TryGetProperty("skipped", out var skip) && skip.ValueKind == JsonValueKind.True;
            var note = test.TryGetProperty("note", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
            results.Add(new ComponentResult(name.GetString()!, ok.GetBoolean(), skipped, note));
        }
        return results;
    }

    public static void ValidateSubmission(TestSubmission submission)
    {
        if (submission.Passed is null || submission.PassedCount is null or < 0
            || submission.FailedCount is null or < 0 || submission.Details?.Length > 16000)
            throw new ArgumentException("Rapporten skal have gyldigt resultat og antal beståede og fejlede tests.");
        var components = Components(submission.Report);
        if (components.Count is < 1 or > 32
            || submission.Report!.Value.GetProperty("tests").GetArrayLength() != components.Count
            || submission.Report.Value.GetProperty("tests").EnumerateArray().Any(test =>
                test.TryGetProperty("skipped", out var skipped) && skipped.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            || components.Any(component => string.IsNullOrWhiteSpace(component.Name)
                || component.Name.Length > 80 || component.Note?.Length > 512)
            || components.Select(component => component.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != components.Count
            || components.Count(component => component.Ok && !component.Skipped) != submission.PassedCount
            || components.Count(component => !component.Ok && !component.Skipped) != submission.FailedCount
            || submission.Passed != (submission.FailedCount == 0))
            throw new ArgumentException("Komponentresultaterne skal være entydige og stemme med rapportens opsummering. Ikke testede komponenter tælles ikke med.");
    }

    public static JsonElement? ParseReport(string? reportJson, string? details)
    {
        var json = reportJson ?? details;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return root.TryGetProperty("report", out var report) && report.ValueKind == JsonValueKind.Object
                ? report.Clone() : root.Clone();
        }
        catch (JsonException) { return null; }
    }

    public static TestResult Result(HardwareTestReport report)
    {
        var components = Components(ParseReport(report.ReportJson, report.Details));
        return new TestResult(report.Id, report.TestedAt,
            report.Passed && report.FailedCount == 0 && !components.Any(component => !component.Ok && !component.Skipped),
            report.PassedCount, report.FailedCount, components, report.LegacyReportId is not null, components.Count > 0);
    }
}
