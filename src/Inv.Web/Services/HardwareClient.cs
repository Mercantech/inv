using System.Net;
using System.Text.Json;
using Inv.Web.Models;

namespace Inv.Web.Services;

public sealed class HardwareClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public async Task<List<DeviceResult>> GetDevicesAsync(Guid? equipmentId = null) =>
        await ReadAsync<List<DeviceResult>>(await auth.GetAsync("hardware/devices" +
            (equipmentId.HasValue ? $"?equipmentId={equipmentId}" : "")));

    public async Task<List<TestResult>> GetTestsAsync(string samdId, int skip = 0) =>
        await ReadAsync<List<TestResult>>(await auth.GetAsync($"hardware/devices/{Uri.EscapeDataString(samdId)}/tests?skip={skip}&take=20"));

    public async Task LinkAsync(string samdId, Guid? equipmentId) =>
        EnsureSuccess(await auth.PostAsync($"hardware/devices/{Uri.EscapeDataString(samdId)}/equipment", new { equipmentId }));

    public async Task CreateEquipmentAsync(string samdId) =>
        EnsureSuccess(await auth.PostAsync($"hardware/devices/{Uri.EscapeDataString(samdId)}/create-equipment", new { }));

    public async Task<ImportResult> ImportAsync(JsonElement devices) =>
        await ReadAsync<ImportResult>(await auth.PostAsync("hardware/import", devices));

    public async Task RegisterDeviceAsync(string samdId, string? mac, string? eccSerial, string? label) =>
        EnsureSuccess(await auth.PostAsync("hardware/devices", new
        {
            samd_id = samdId,
            mac,
            ecc_serial = eccSerial,
            label
        }));

    public async Task<TestResult> SubmitTestAsync(string samdId, bool passed, int passedCount, int failedCount, object report) =>
        await ReadAsync<TestResult>(await auth.PostAsync($"hardware/devices/{Uri.EscapeDataString(samdId)}/tests", new
        {
            passed,
            passed_count = passedCount,
            failed_count = failedCount,
            report
        }));

    private static Task<T> ReadAsync<T>(AuthResponse response)
    {
        EnsureSuccess(response);
        return Task.FromResult(JsonSerializer.Deserialize<T>(response.Body, Options)
            ?? throw new JsonException("API'et returnerede et ugyldigt resultat."));
    }

    private static void EnsureSuccess(AuthResponse response)
    {
        if (response.Status is >= 200 and < 300) return;
        var message = response.Status switch
        {
            401 => "Dit login er udløbet. Log ind igen.",
            403 => "Du har ikke adgang til denne handling.",
            404 => "Boardet eller udstyret findes ikke længere.",
            409 => "Udstyret er allerede tilknyttet, eller serienummeret findes. Genindlæs og vælg eksisterende udstyr.",
            400 => TryProblemDetail(response.Body) ?? "Rapporten er ugyldig (komponentresultater stemmer ikke overens).",
            413 => "Importen er for stor (maks. 10 MB). Brug importværktøjet fra terminalen.",
            _ => "Hardwareoplysningerne kunne ikke hentes eller gemmes. Prøv igen."
        };
        throw new HttpRequestException(message, null, (HttpStatusCode)response.Status);
    }

    private static string? TryProblemDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in errors.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
                        return prop.Value[0].GetString();
                }
            }
            if (root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString();
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                return title.GetString();
        }
        catch (JsonException) { /* ignore */ }
        return null;
    }
}
