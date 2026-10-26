using System.Text.Json;

namespace Inv.Api.Features.Loans;

public sealed record BorrowerResponse(Guid Subject, string Name, string? Email, bool IsDisabled);

public interface IBorrowerDirectory
{
    Task<IReadOnlyList<BorrowerResponse>> GetAsync(CancellationToken cancellationToken);
}

public sealed class DirectoryUnavailableException : Exception;

// Auths administrative svar indeholder mere end låneropslaget behøver.
// Kun disse fire felter må sendes videre til frontend.
public sealed class BorrowerDirectory(HttpClient http, IConfiguration configuration) : IBorrowerDirectory
{
    public async Task<IReadOnlyList<BorrowerResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var key = configuration["AUTH_MCP_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)
            || !Uri.TryCreate(configuration["AUTH_ISSUER"], UriKind.Absolute, out var issuer)
            || issuer.Scheme != "https")
            throw new DirectoryUnavailableException();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(issuer, "/api/admin/users-directory"));
        request.Headers.Add("x-api-key", key);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<List<DirectoryRow>>(cancellationToken);
            if (rows is null || rows.Any(row => row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.DisplayName)))
                throw new DirectoryUnavailableException();
            return rows.Select(row => new BorrowerResponse(row.Id, row.DisplayName, row.Email, row.IsDisabled))
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Subject).ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
                                        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Auth-svar, nøgler og brugerdata må ikke lække gennem fejlbeskeden.
            throw new DirectoryUnavailableException();
        }
    }

    private sealed record DirectoryRow(Guid Id, string DisplayName, string? Email, bool IsDisabled);
}
