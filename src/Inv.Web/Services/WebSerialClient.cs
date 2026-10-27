using Microsoft.JSInterop;

namespace Inv.Web.Services;

public sealed class WebSerialClient(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? module;
    private Task<IJSObjectReference>? flashModule;
    private DotNetObjectReference<WebSerialClient>? selfRef;

    private Task<IJSObjectReference> Module =>
        module ??= js.InvokeAsync<IJSObjectReference>("import", "./webserial.js?v=3").AsTask();

    private Task<IJSObjectReference> FlashModule =>
        flashModule ??= js.InvokeAsync<IJSObjectReference>("import", "./firmware-flash.js?v=1").AsTask();

    public event Action<string>? LineReceived;
    public event Action<string>? ErrorReceived;
    public event Action<string>? FlashLog;
    public event Action<int, int>? FlashProgress;

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

    public async Task FlashFirmwareAsync()
    {
        await DisconnectAsync();
        selfRef?.Dispose();
        selfRef = DotNetObjectReference.Create(this);
        await (await FlashModule).InvokeVoidAsync("flashFirmware", selfRef);
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

    [JSInvokable]
    public Task OnFlashLog(string message)
    {
        FlashLog?.Invoke(message);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnFlashProgress(int done, int total)
    {
        FlashProgress?.Invoke(done, total);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try { await DisconnectAsync(); }
        catch (JSDisconnectedException) { /* page unloading */ }
        foreach (var holder in new[] { module, flashModule })
        {
            if (holder is null) continue;
            try { await (await holder).DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        module = flashModule = null;
    }
}
