using System.Net;
using System.Text.Json;
using Inv.Web.Models;

namespace Inv.Web.Services;

public sealed class IssueCategoryClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<IssueCategoryResponse>> GetAllAsync()
    {
        var response = await auth.GetAsync("issue-categories/");
        if (response.Status != 200)
            throw new HttpRequestException("Fejlkategorier kunne ikke hentes.", null, (HttpStatusCode)response.Status);

        return JsonSerializer.Deserialize<List<IssueCategoryResponse>>(response.Body, JsonOptions)
            ?? throw new JsonException();
    }

    public Task<AuthResponse> CreateAsync(IssueCategoryFormModel model) =>
        auth.PostAsync("issue-categories", model);

    public Task<AuthResponse> UpdateAsync(Guid id, IssueCategoryFormModel model) =>
        auth.PutAsync($"issue-categories/{id}", model);

    public Task<AuthResponse> DeleteAsync(Guid id) =>
        auth.DeleteAsync($"issue-categories/{id}");
}
