using Microsoft.JSInterop;

namespace Inv.Web.Services;

public sealed class WebSerialClient(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? module;
    private Task<IJSObjectReference>? flashModule;
    private DotNetObjectReference<WebSerialClient>? selfRef;

    private Task<IJSObjectReference> Module =>
        module ??= js.InvokeAsync<IJSObjectReference>("import", "./webserial.js").AsTask();

    private Task<IJSObjectReference> FlashModule =>
        flashModule ??= js.InvokeAsync<IJSObjectReference>("import", "./firmware-flash.js").AsTask();

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

    /// <summary>Forudindlæs flash-modul, så klik → requestPort ikke venter på import.</summary>
    public async Task WarmFlashModuleAsync()
    {
        selfRef ??= DotNetObjectReference.Create(this);
        _ = await FlashModule;
    }

    /// <summary>Trin 1: vælg app-port og send board i bootloader (skal kaldes fra et klik).</summary>
    public async Task PrepareFlashAsync()
    {
        // Afbryd serial efter JS er i gang med port-dialog hvis muligt — men modul skal være varmt.
        selfRef ??= DotNetObjectReference.Create(this);
        var flash = await FlashModule;
        try { await DisconnectAsync(); }
        catch (JSException) { /* ingen aktiv port */ }
        // requestPort er første handling i prepareFlash (ingen awaits før dialogen).
        await flash.InvokeVoidAsync("prepareFlash", selfRef);
    }

    /// <summary>Trin 2: vælg bootloader-port og skriv firmware (nyt klik). Minimal C# før JS.</summary>
    public async Task CompleteFlashAsync()
    {
        selfRef ??= DotNetObjectReference.Create(this);
        await (await FlashModule).InvokeVoidAsync("completeFlash", selfRef);
    }

    public async Task CancelFlashAsync()
    {
        if (flashModule is null) return;
        await (await FlashModule).InvokeVoidAsync("cancelFlash");
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
        try
        {
            if (flashModule is not null)
                await (await FlashModule).InvokeVoidAsync("cancelFlash");
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }

        foreach (var holder in new[] { module, flashModule })
        {
            if (holder is null) continue;
            try { await (await holder).DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        module = flashModule = null;
    }
}
