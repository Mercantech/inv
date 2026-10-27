using System.Net;
using System.Text.Json;
using Inv.Web.Models;

namespace Inv.Web.Services;

public sealed class IssueClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<EquipmentIssueResponse>> GetForEquipmentAsync(Guid equipmentId)
    {
        var response = await auth.GetAsync($"equipment/{equipmentId}/issues");
        if (response.Status != 200)
            throw new HttpRequestException("Fejlrapporter kunne ikke hentes.", null, (HttpStatusCode)response.Status);

        return JsonSerializer.Deserialize<List<EquipmentIssueResponse>>(response.Body, JsonOptions)
            ?? throw new JsonException();
    }

    public Task<AuthResponse> CreateAsync(Guid equipmentId, CreateIssueFormModel model) =>
        auth.PostAsync($"equipment/{equipmentId}/issues", model);

    public Task<AuthResponse> UpdateAsync(Guid equipmentId, Guid issueId, UpdateIssueFormModel model) =>
        auth.PutAsync($"equipment/{equipmentId}/issues/{issueId}", model);

    public Task<AuthResponse> DeleteAsync(Guid equipmentId, Guid issueId) =>
        auth.DeleteAsync($"equipment/{equipmentId}/issues/{issueId}");
}
