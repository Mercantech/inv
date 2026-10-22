namespace Inv.Web.Services;

public sealed class ApiStatusClient(MercantecAuthClient auth)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await auth.GetAsync("health/ready");
            return response.Status == 200
                && string.Equals(response.Body, "Healthy", StringComparison.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (Microsoft.JSInterop.JSException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
