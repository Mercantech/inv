using System.Net;
using System.Text.Json;
using Inv.Web.Models;

namespace Inv.Web.Services;

public sealed class LoanClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<bool> CanManageAsync() => (await auth.GetAsync("loan-access")).Status == 200;

    public async Task<LoanHistoryResponse> GetHistoryAsync(Guid equipmentId)
    {
        var response = await auth.GetAsync($"equipment/{equipmentId}/loans");
        EnsureSuccess(response);
        return JsonSerializer.Deserialize<LoanHistoryResponse>(response.Body, JsonOptions) ?? throw new JsonException();
    }

    public async Task<ActiveLoansResponse> GetActiveAsync(string? search = null)
    {
        var response = await auth.GetAsync($"loans?search={Uri.EscapeDataString(search?.Trim() ?? string.Empty)}");
        EnsureSuccess(response);
        return JsonSerializer.Deserialize<ActiveLoansResponse>(response.Body, JsonOptions)
            ?? throw new JsonException();
    }

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

    public Task<AuthResponse> ReturnAsync(Guid equipmentId, Guid loanId, string? returnNote, int? condition) =>
        auth.PostAsync($"equipment/{equipmentId}/loans/return", new { LoanId = loanId, ReturnNote = returnNote, Condition = condition });

    private static void EnsureSuccess(AuthResponse response)
    {
        if (response.Status != 200)
            throw new HttpRequestException("Oplysningerne kunne ikke hentes.", null, (HttpStatusCode)response.Status);
    }
}
