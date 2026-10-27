using System.Net;
using System.Text.Json;
using Inv.Web.Models;

namespace Inv.Web.Services;

public sealed class EquipmentClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<List<EquipmentResponse>> GetAllAsync(
        string? search = null,
        Guid? categoryId = null,
        int? condition = null)
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        if (categoryId.HasValue)
        {
            parameters.Add($"categoryId={categoryId.Value}");
        }

        if (condition.HasValue)
        {
            parameters.Add(
                $"condition={condition.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        var url = "equipment/";

        if (parameters.Count > 0)
        {
            url += "?" + string.Join("&", parameters);
        }

        var response = await auth.GetAsync(url);

        if (response.Status != 200)
        {
            throw new HttpRequestException(
                "Udstyret kunne ikke hentes.",
                null,
                (HttpStatusCode)response.Status);
        }

        return JsonSerializer.Deserialize<List<EquipmentResponse>>(
            response.Body,
            JsonOptions) ?? throw new JsonException(
            "API'et returnerede en ugyldig udstyrsliste.");
    }

    public async Task<EquipmentResponse?> GetByIdAsync(Guid id)
    {
        var response = await auth.GetAsync($"equipment/{id}");

        if (response.Status == 404)
        {
            return null;
        }

        if (response.Status != 200)
        {
            throw new HttpRequestException(
                "Udstyret kunne ikke hentes.",
                null,
                (HttpStatusCode)response.Status);
        }

        return JsonSerializer.Deserialize<EquipmentResponse>(
            response.Body,
            JsonOptions) ?? throw new JsonException(
            "API'et returnerede ugyldige udstyrsoplysninger.");
    }

    public async Task<AuthResponse> UpdateAsync(Guid id, EquipmentFormModel equipmentModel)
        => await auth.PutAsync($"equipment/{id}", equipmentModel);

    public async Task<AuthResponse> CreateAsync(
        EquipmentFormModel equipmentModel)
    {
        return await auth.PostAsync("equipment/", equipmentModel);
    }

    public Task<AuthResponse> DeleteAsync(Guid id) =>
        auth.DeleteAsync($"equipment/{id}");
}
