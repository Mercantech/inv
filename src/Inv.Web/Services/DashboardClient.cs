using System.Net;
using System.Text.Json;
using Inv.Web.Models;
namespace Inv.Web.Services;
public sealed class DashboardClient(MercantecAuthClient auth)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public async Task<DashboardResponse> GetAsync()
    {
        var response = await auth.GetAsync("dashboard");
        if (response.Status != 200) throw new HttpRequestException("Dashboardet kunne ikke hentes.", null, (HttpStatusCode)response.Status);
        return JsonSerializer.Deserialize<DashboardResponse>(response.Body, Options) ?? throw new JsonException();
    }
}
