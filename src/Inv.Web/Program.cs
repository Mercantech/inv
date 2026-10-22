using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Inv.Web;
using Inv.Web.Services;
using System.Text.Json;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

using (var configurationClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
{
    await using var configuration = await configurationClient.GetStreamAsync("client-config.json");
    using var document = await JsonDocument.ParseAsync(configuration);
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ApiBaseUrl"] = document.RootElement.GetProperty("ApiBaseUrl").GetString()
    });
}

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "api/";
var apiBaseAddress = new Uri(new Uri(builder.HostEnvironment.BaseAddress), apiBaseUrl);

if (apiBaseAddress.Scheme is not ("http" or "https") || !apiBaseAddress.AbsolutePath.EndsWith('/'))
{
    throw new InvalidOperationException("ApiBaseUrl skal være en HTTP(S)-adresse med afsluttende skråstreg.");
}

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = apiBaseAddress,
    Timeout = TimeSpan.FromSeconds(10)
});
builder.Services.AddScoped<ApiStatusClient>();

await builder.Build().RunAsync();
