namespace Inv.Web.Services;

public sealed class ApiStatusClient(HttpClient httpClient)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("health/ready", cancellationToken);
            return response.IsSuccessStatusCode
                && string.Equals(await response.Content.ReadAsStringAsync(cancellationToken), "Healthy", StringComparison.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
