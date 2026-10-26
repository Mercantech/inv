using System.Net;
using System.Text.Json;
using Inv.Web.Models;

namespace Inv.Web.Services;

public sealed class LoanClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<BorrowerResponse>> GetBorrowersAsync()
    {
        var response = await auth.GetAsync("borrowers");
        EnsureSuccess(response);
        return JsonSerializer.Deserialize<List<BorrowerResponse>>(response.Body, JsonOptions)
            ?? throw new JsonException();
    }

    public async Task<LoanResponse?> GetCurrentAsync(Guid equipmentId)
    {
        var response = await auth.GetAsync($"equipment/{equipmentId}/loan");
        if (response.Status == 204) return null;
        EnsureSuccess(response);
        return JsonSerializer.Deserialize<LoanResponse>(response.Body, JsonOptions)
            ?? throw new JsonException();
    }

    public Task<AuthResponse> CreateAsync(Guid equipmentId, Guid borrowerSubject, string? note) =>
        auth.PostAsync($"equipment/{equipmentId}/loans", new { BorrowerSubject = borrowerSubject, Note = note });

    private static void EnsureSuccess(AuthResponse response)
    {
        if (response.Status != 200)
            throw new HttpRequestException("Oplysningerne kunne ikke hentes.", null, (HttpStatusCode)response.Status);
    }
}
