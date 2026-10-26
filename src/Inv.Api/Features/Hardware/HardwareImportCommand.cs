using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Inv.Api.Features.Hardware;

internal static class HardwareImportCommand
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var source = configuration["OPLA_IMPORT_SOURCE"];
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("Sæt OPLA_IMPORT_SOURCE til et JSON-eksport eller en HTTPS-adresse til Oplà-systemets /api/devices.");

        await using var scope = services.CreateAsyncScope();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        await using var stream = Uri.TryCreate(source, UriKind.Absolute, out var url) && url.Scheme == "https"
            ? await client.GetStreamAsync(url)
            : File.OpenRead(source);
        var devices = await JsonSerializer.DeserializeAsync<List<LegacyDevice>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Importfilen skal indeholde en liste af boards med tests.");
        var result = await scope.ServiceProvider.GetRequiredService<HardwareStore>().ImportAsync(devices, CancellationToken.None);
        logger.LogInformation("Oplà-import: {DevicesAdded} nye boards, {ReportsAdded} nye rapporter, {ReportsSkipped} rapporter allerede importeret.",
            result.DevicesAdded, result.ReportsAdded, result.ReportsSkipped);
    }
}
