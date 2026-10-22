using Microsoft.JSInterop;

namespace Inv.Web.Services;

public sealed record AuthConfiguration(string Authority, string ClientId, string RedirectUri, string ApiBaseUrl);
public sealed record AuthResponse(int Status, string Body);

public sealed class MercantecAuthClient(IJSRuntime js, AuthConfiguration configuration) : IAsyncDisposable
{
    private Task<IJSObjectReference>? module;
    private Task<IJSObjectReference> Module => module ??= js.InvokeAsync<IJSObjectReference>("import", "./auth.js").AsTask();

    public async Task LoginAsync() => await (await Module).InvokeVoidAsync("login", configuration);
    public async Task CompleteLoginAsync() => await (await Module).InvokeVoidAsync("callback", configuration);
    public async Task LogoutAsync() => await (await Module).InvokeVoidAsync("logout", configuration);
    public async Task<bool> IsSignedInAsync() => await (await Module).InvokeAsync<bool>("isSignedIn");
    public async Task<AuthResponse> GetAsync(string relativePath) => await (await Module).InvokeAsync<AuthResponse>(
        "request", configuration, new Uri(new Uri(configuration.ApiBaseUrl), relativePath).AbsoluteUri);

    public async ValueTask DisposeAsync()
    {
        if (module is not null) await (await module).DisposeAsync();
    }
}
