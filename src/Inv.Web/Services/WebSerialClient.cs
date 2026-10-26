using Microsoft.JSInterop;

namespace Inv.Web.Services;

public sealed class WebSerialClient(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? module;
    private DotNetObjectReference<WebSerialClient>? selfRef;
    private Task<IJSObjectReference> Module =>
        module ??= js.InvokeAsync<IJSObjectReference>("import", "./webserial.js?v=1").AsTask();

    public event Action<string>? LineReceived;
    public event Action<string>? ErrorReceived;

    public async Task<bool> IsSupportedAsync() => await (await Module).InvokeAsync<bool>("isSupported");

    public async Task ConnectAsync()
    {
        selfRef?.Dispose();
        selfRef = DotNetObjectReference.Create(this);
        await (await Module).InvokeVoidAsync("connect", selfRef);
    }

    public async Task WriteLineAsync(string text) =>
        await (await Module).InvokeVoidAsync("writeLine", text);

    public async Task DisconnectAsync()
    {
        if (module is not null)
            await (await Module).InvokeVoidAsync("disconnect");
        selfRef?.Dispose();
        selfRef = null;
    }

    [JSInvokable]
    public Task OnSerialLine(string line)
    {
        LineReceived?.Invoke(line);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSerialError(string message)
    {
        ErrorReceived?.Invoke(message);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try { await DisconnectAsync(); }
        catch (JSDisconnectedException) { /* page unloading */ }
        if (module is not null)
        {
            try { await (await module).DisposeAsync(); }
            catch (JSDisconnectedException) { }
            module = null;
        }
    }
}
