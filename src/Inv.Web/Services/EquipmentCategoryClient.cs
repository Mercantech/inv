using System.Net;
using System.Text.Json;
using Inv.Web.Models;

namespace Inv.Web.Services;

public sealed class EquipmentCategoryClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<EquipmentcategoryResponse>> GetAllAsync()
    {
        var response = await auth.GetAsync("equipment-categories/");

        if (response.Status != 200)
        {
            throw new HttpRequestException(
                "Kategorierne kunne ikke hentes.",
                null,
                (HttpStatusCode)response.Status);
        }

        return JsonSerializer.Deserialize<List<EquipmentcategoryResponse>>(
            response.Body,
            JsonOptions) ?? throw new JsonException("API'et returnerede en ugyldig kategoriliste.");
    }

    public async Task<AuthResponse> CreateAsync(
        CreateEquipmentCategoryModel categoryModel)
    {
        return await auth.PostAsync("equipment-categories", categoryModel);
    }
}