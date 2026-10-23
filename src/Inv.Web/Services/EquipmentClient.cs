using System.Net;
using System.Text.Json;
using Inv.Web.Models;

namespace Inv.Web.Services;

public sealed class EquipmentClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<List<EquipmentResponse>> GetAllAsync()
    {
        var response = await auth.GetAsync("equipment/");

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

    public async Task<AuthResponse> CreateAsync(
        CreateEquipmentModel equipmentModel)
    {
        return await auth.PostAsync("equipment/", equipmentModel);
    }
}