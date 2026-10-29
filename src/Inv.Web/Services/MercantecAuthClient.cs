using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace Inv.Web.Services;

public sealed record AuthConfiguration(string Authority, string ClientId, string RedirectUri, string ApiBaseUrl);

public sealed record AuthResponse(int Status, string Body);

public sealed class MercantecAuthClient(IJSRuntime js, AuthConfiguration configuration, NavigationManager navigation) : IAsyncDisposable
{
    private Task<IJSObjectReference>? module;

    private Task<IJSObjectReference> Module =>
        module ??= js.InvokeAsync<IJSObjectReference>("import", "./auth.js?v=3").AsTask();

    public async Task LoginAsync(string? returnUrl = null) => await (await Module).InvokeVoidAsync("login", configuration, returnUrl);
    public async Task<string> CompleteLoginAsync() => await (await Module).InvokeAsync<string>("callback", configuration);
    public async Task LogoutAsync()
    {
        await (await Module).InvokeVoidAsync("clearSession");
        navigation.NavigateTo("login", forceLoad: true, replace: true);
    }
    public async Task<bool> IsSignedInAsync() => await (await Module).InvokeAsync<bool>("isSignedIn");

    public async Task<AuthResponse> GetAsync(string relativePath) => await (await Module).InvokeAsync<AuthResponse>(
        "request", configuration, new Uri(new Uri(configuration.ApiBaseUrl), relativePath).AbsoluteUri);

    // Sender data som JSON med brugerens login-token.
    // JavaScript-funktionen håndterer også fornyelse af token.
    public async Task<AuthResponse> PostAsync<T>(string relativePath, T body)
    {
        var url = new Uri(
            new Uri(configuration.ApiBaseUrl),
            relativePath).AbsoluteUri;

        return await (await Module).InvokeAsync<AuthResponse>(
            "request",
            configuration,
            url,
            "POST",
            body);
    }

    public async Task<AuthResponse> PutAsync<T>(string relativePath, T body) =>
        await (await Module).InvokeAsync<AuthResponse>(
            "request", configuration,
            new Uri(new Uri(configuration.ApiBaseUrl), relativePath).AbsoluteUri,
            "PUT", body);

    public async Task<AuthResponse> DeleteAsync(string relativePath) =>
        await (await Module).InvokeAsync<AuthResponse>(
            "request", configuration,
            new Uri(new Uri(configuration.ApiBaseUrl), relativePath).AbsoluteUri,
            "DELETE");

    public async ValueTask DisposeAsync()
    {
        if (module is not null) await (await module).DisposeAsync();
    }
}
