using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Inv.Api.Features.Equipment;
using Inv.Api.Features.Issues;
using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Inv.Web.Components;
using Inv.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Xunit;
using IssueResponse = Inv.Api.Features.Issues.EquipmentIssueResponse;

namespace Inv.Database.Tests;

public sealed class IssueWorkflowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid OtherManager = Guid.NewGuid();

    [Fact]
    public async Task Full_workflow_preserves_independent_reports_notes_and_server_resolution_details()
    {
        await using var app = await StartAsync();
        using var manager = Client(app, "manager");
        using var otherManager = Client(app, "other");
        var (equipment, first, second) = await SeedAsync();
        var path = $"/api/equipment/{equipment.Id}/issues/{first.Id}";
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync(path, new UpdateIssueRequest(null, null, "InProgress", null))).StatusCode);
        foreach (var note in new[] { "", "   ", new string('x', 4001) })
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(path, new UpdateIssueRequest(null, null, "Resolved", note))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(path, new UpdateIssueRequest(null, null, "Invalid", null))).StatusCode);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var response = await manager.PutAsJsonAsync(path, new UpdateIssueRequest(null, null, "Resolved", "  Stik udskiftet  "));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var solved = (await response.Content.ReadFromJsonAsync<IssueResponse>())!;
        Assert.Equal("Stik udskiftet", solved.ResolutionNote);
        Assert.Equal(Manager, solved.ResolvedBySubject);
        Assert.InRange(solved.ResolvedAt!.Value, before, DateTimeOffset.UtcNow.AddSeconds(1));
        var edited = (await (await otherManager.PutAsJsonAsync(path,
            new UpdateIssueRequest(null, null, "Resolved", "Stik udskiftet og afprøvet"))).Content.ReadFromJsonAsync<IssueResponse>())!;
        // PostgreSQL gemmer mikrosekunder; første HTTP-svar kan have 100 ns-præcision.
        Assert.InRange(Math.Abs((solved.ResolvedAt!.Value - edited.ResolvedAt!.Value).Ticks), 0L, 9L);
        Assert.Equal(Manager, edited.ResolvedBySubject);
        Assert.Equal(1, (await manager.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.FaultCount);
        var reopened = (await (await otherManager.PutAsJsonAsync(path,
            new UpdateIssueRequest(null, null, "Open", edited.ResolutionNote))).Content.ReadFromJsonAsync<IssueResponse>())!;
        Assert.Equal("Open", reopened.Status);
        Assert.Null(reopened.ResolvedAt);
        Assert.Null(reopened.ResolvedBySubject);
        Assert.Equal(edited.ResolutionNote, reopened.ResolutionNote);
        Assert.Equal(2, (await manager.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.FaultCount);
        var solvedAgain = (await (await otherManager.PutAsJsonAsync(path,
            new UpdateIssueRequest(null, null, "Resolved", "Repareret igen"))).Content.ReadFromJsonAsync<IssueResponse>())!;
        Assert.Equal(OtherManager, solvedAgain.ResolvedBySubject);
        await using var db = postgres.CreateContext();
        var untouched = await db.EquipmentIssues.AsNoTracking().SingleAsync(i => i.Id == second.Id);
        Assert.Equal(EquipmentIssueStatus.Open, untouched.Status);
        Assert.Null(untouched.ResolutionNote);
        Assert.Equal(2, await db.EquipmentIssues.CountAsync(i => i.EquipmentId == equipment.Id));
        Assert.Equal(first.ReportedBySubject, solvedAgain.ReportedBySubject);
        Assert.Equal(first.Description, solvedAgain.Description);
    }

    [Fact]
    public async Task Users_cannot_change_reports_and_invalid_requests_do_not_change_saved_state()
    {
        await using var app = await StartAsync();
        var (equipment, first, _) = await SeedAsync();
        var path = $"/api/equipment/{equipment.Id}/issues/{first.Id}";
        using var user = Client(app, "user");
        using var anonymous = app.GetTestClient();
        var update = new UpdateIssueRequest(null, null, "Resolved", "Repareret");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync(path, update)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(path, update)).StatusCode);
        using var manager = Client(app, "manager");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(path, update with { ResolutionNote = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsJsonAsync($"/api/equipment/{equipment.Id}/issues/{Guid.NewGuid()}", update)).StatusCode);
        await using var db = postgres.CreateContext();
        var saved = await db.EquipmentIssues.SingleAsync(i => i.Id == first.Id);
        Assert.Equal(EquipmentIssueStatus.Open, saved.Status);
        Assert.Null(saved.ResolvedAt);
        Assert.Null(saved.ResolvedBySubject);
    }

    [Fact]
    public async Task Rendered_editor_validates_cancels_saves_custom_note_and_reopens_through_real_api()
    {
        await using var app = await StartAsync();
        var (equipment, first, _) = await SeedAsync();
        using var client = Client(app, "manager");
        var bridge = new ApiBridge(client);
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IJSRuntime>(bridge);
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton(new AuthConfiguration("https://auth.mercantec.tech", "test", "http://localhost/auth/callback", "http://localhost/api/"));
        services.AddScoped<MercantecAuthClient>(); services.AddScoped<AdminAccess>();
        services.AddScoped<IssueClient>(); services.AddScoped<Inv.Web.Services.IssueCategoryClient>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        EquipmentIssues? component = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<EditorProbe>(ParameterView.FromDictionary(new Dictionary<string, object?>
        { ["EquipmentId"] = equipment.Id, ["Capture"] = (Action<EquipmentIssues>)(c => component = c) })));
        var row = (await client.GetFromJsonAsync<List<Inv.Web.Models.EquipmentIssueResponse>>($"/api/equipment/{equipment.Id}/issues"))!.Single(i => i.Id == first.Id);
        async Task Act(string method, params object?[] args) => await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = typeof(EquipmentIssues).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            if (result is Task task) await task;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
        });
        void Field(string name, object value) => typeof(EquipmentIssues).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
        async Task<string> Html() => await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        await Act("StartEdit", row, "Resolved");
        Assert.Contains("Løsningsnote (påkrævet)", await Html());
        Assert.Contains("maxlength=\"4000\"", await Html());
        await Act("SaveResolutionAsync", row);
        Assert.Contains("Angiv en løsningsnote", await Html());
        Field("resolutionNote", new string('x', 4001));
        await Act("SaveResolutionAsync", row);
        Assert.Contains("Angiv en løsningsnote", await Html());
        Assert.Equal(0, bridge.PutCount);
        Field("resolutionNote", "Skal annulleres");
        await Act("CancelEdit");
        Assert.DoesNotContain("Behandl fejlrapport</legend>", await Html());
        Assert.Equal(0, bridge.PutCount);
        await Act("StartEdit", row, "Resolved");
        Field("resolutionNote", "Stik repareret fra formularen");
        await Act("SaveResolutionAsync", row);
        Assert.Equal(1, bridge.PutCount);
        Assert.Contains("Stik repareret fra formularen", await Html());
        Assert.Contains("Genåbn", await Html());
        row = (await client.GetFromJsonAsync<List<Inv.Web.Models.EquipmentIssueResponse>>($"/api/equipment/{equipment.Id}/issues"))!.Single(i => i.Id == first.Id);
        await Act("StartEdit", row, "Open");
        await Act("SaveResolutionAsync", row);
        Assert.Contains("Seneste løsningsnote: Stik repareret fra formularen", await Html());
        Assert.Contains("Åben", await Html());
        await using var db = postgres.CreateContext();
        Assert.Equal(EquipmentIssueStatus.Open, (await db.EquipmentIssues.SingleAsync(i => i.Id == first.Id)).Status);
    }

    public sealed class EditorProbe : EquipmentIssues
    {
        [Parameter] public Action<EquipmentIssues> Capture { get; set; } = null!;
        protected override void OnInitialized() => Capture(this);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class ApiBridge(HttpClient client) : IJSRuntime, IJSObjectReference
    {
        public int PutCount { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            if (identifier == "import") return (TValue)(object)this;
            if (identifier != "request") throw new NotSupportedException(identifier);
            var url = (string)args![1]!;
            var method = args.Length > 2 ? (string)args[2]! : "GET";
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (args.Length > 3) request.Content = JsonContent.Create(args[3]);
            if (method == "PUT") PutCount++;
            using var response = await client.SendAsync(request, ct);
            return (TValue)(object)new AuthResponse((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private async Task<(Equipment Equipment, EquipmentIssue First, EquipmentIssue Second)> SeedAsync()
    {
        await using var db = postgres.CreateContext();
        var equipment = new Equipment { Name = "Fejlflow", SerialNumber = Guid.NewGuid().ToString(), Category = new EquipmentCategory { Name = Guid.NewGuid().ToString() } };
        var category = new IssueCategory { Name = Guid.NewGuid().ToString() };
        var first = new EquipmentIssue { Equipment = equipment, Category = category, Description = "Stik defekt", ReportedBySubject = Manager };
        var second = new EquipmentIssue { Equipment = equipment, Category = category, Description = "Kabinet revnet", ReportedBySubject = Manager };
        db.AddRange(first, second); await db.SaveChangesAsync();
        return (equipment, first, second);
    }
    private static HttpClient Client(WebApplication app, string role)
    {
        var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-Test-Role", role); return client;
    }
    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<InventoryDbContext>(o => { using var db = postgres.CreateContext(); o.UseNpgsql(db.Database.GetConnectionString()); });
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestUserHandler>("test", _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy("EquipmentManager", p => p.RequireRole("EquipmentManager", "Admin")));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/api/manager-access", () => Microsoft.AspNetCore.Http.Results.Ok()).RequireAuthorization("EquipmentManager");
        app.MapEquipmentEndpoints(); app.MapIssueEndpoints(); await app.StartAsync(); return app;
    }
    private sealed class TestUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new("sub", (role == "other" ? OtherManager : Manager).ToString()) };
            if (role != "user") claims.Add(new(ClaimTypes.Role, role == "other" ? "Admin" : "EquipmentManager"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
