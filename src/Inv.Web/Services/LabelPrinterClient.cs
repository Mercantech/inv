using Microsoft.JSInterop;
namespace Inv.Web.Services;
public sealed record LabelOptions(string PublicBaseUrl);
public sealed record LabelPreview(string PreviewUrl, string Url, int ShortId);
public sealed class LabelPrinterClient(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? module;
    private Task<IJSObjectReference> Module => module ??= js.InvokeAsync<IJSObjectReference>("import", "./letratag.js?v=1").AsTask();
    public async Task<LabelPreview> PrepareAsync(int shortId, string name, string baseUrl) =>
        await (await Module).InvokeAsync<LabelPreview>("prepare", shortId, name, baseUrl);
    public async Task<bool> IsSupportedAsync() => await (await Module).InvokeAsync<bool>("isSupported");
    public async Task<string?> ConnectionAsync() => await (await Module).InvokeAsync<string?>("connection");
    public async Task<string> ConnectAsync() => await (await Module).InvokeAsync<string>("connect");
    public async Task DisconnectAsync() => await (await Module).InvokeVoidAsync("disconnect");
    public async Task<string> PrintAsync(int shortId, string url) => await (await Module).InvokeAsync<string>("print", shortId, url);
    public async ValueTask DisposeAsync() { if (module is not null) await (await module).DisposeAsync(); }
}
