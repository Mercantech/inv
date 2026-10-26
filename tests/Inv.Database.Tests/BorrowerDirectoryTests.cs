using System.Net;
using System.Text.Json;
using Inv.Api.Features.Loans;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Inv.Database.Tests;

public sealed class BorrowerDirectoryTests
{
    [Fact]
    public async Task Directory_uses_server_key_and_returns_only_borrower_fields_from_all_auth_accounts()
    {
        var subject = Guid.NewGuid();
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://auth.example.test/api/admin/users-directory", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-api-key")));
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent(new[] { new
                {
                    Id = subject, DisplayName = "Uden lokalt login", Email = (string?)null,
                    IsDisabled = false, ClientUsages = Array.Empty<object>(),
                    LinkedProviders = new[] { new { Provider = "private-provider" } }, HasLocalLogin = false
                } })
            };
        });
        using var http = new HttpClient(handler);
        var result = await new BorrowerDirectory(http, Config()).GetAsync(default);
        var borrower = Assert.Single(result);
        Assert.Equal(subject, borrower.Subject);
        Assert.Null(borrower.Email);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(4, json.RootElement[0].EnumerateObject().Count());
        Assert.DoesNotContain("private-provider", json.RootElement.GetRawText());
    }

    [Theory]
    [InlineData(401, "private-auth-error")]
    [InlineData(403, "private-auth-error")]
    [InlineData(500, "private-auth-error")]
    [InlineData(200, "null")]
    [InlineData(200, "invalid-json")]
    [InlineData(200, "[{}]")]
    public async Task Directory_failure_is_sanitized(int status, string body)
    {
        using var http = new HttpClient(new StubHandler(_ => new((HttpStatusCode)status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        }));
        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            new BorrowerDirectory(http, Config()).GetAsync(default));
        Assert.DoesNotContain("private-auth-error", exception.ToString());
        Assert.DoesNotContain("test-key", exception.ToString());
    }

    [Fact]
    public async Task Missing_key_does_not_call_auth()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Must not call")));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            new BorrowerDirectory(http, new ConfigurationBuilder().Build()).GetAsync(default));
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AUTH_ISSUER"] = "https://auth.example.test", ["AUTH_MCP_API_KEY"] = "test-key"
    }).Build();

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
