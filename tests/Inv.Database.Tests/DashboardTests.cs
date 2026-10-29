using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Inv.Api.Features.Dashboard;
using Inv.Api.Features.Equipment;
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

namespace Inv.Database.Tests;

public sealed class DashboardTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Counts_distinct_equipment_current_faults_categories_and_refreshes_after_changes()
    {
        await using var app = await StartAsync();
        using var client = Client(app, "EquipmentManager");
        var empty = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(0, empty.TotalEquipment); Assert.Empty(empty.Categories); Assert.Empty(empty.Attention);
        await using var db = postgres.CreateContext();
        var category = new EquipmentCategory { Name = "Arduino" };
        var other = new EquipmentCategory { Name = "Andet" };
        var items = Enumerable.Range(0, 6).Select(i => new Equipment { Name = "Udstyr " + i,
            SerialNumber = Guid.NewGuid().ToString(), Category = i < 3 ? category : other }).ToArray();
        db.AddRange(items);
        var loans = new[] { new Loan { Equipment = items[1], BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Guid.NewGuid() },
            new Loan { Equipment = items[5], BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Guid.NewGuid() } };
        db.AddRange(loans);
        db.Add(new Loan { Equipment = items[2], BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Guid.NewGuid(),
            LoanedAt = DateTimeOffset.UtcNow.AddDays(-2), ReturnedAt = DateTimeOffset.UtcNow.AddDays(-1), ReturnedBySubject = Guid.NewGuid() });
        var issueCategory = new IssueCategory { Name = "Manuel fejl" };
        var first = new EquipmentIssue { Equipment = items[1], Category = issueCategory, Description = "A", ReportedBySubject = Guid.NewGuid() };
        var second = new EquipmentIssue { Equipment = items[1], Category = issueCategory, Description = "B", Status = EquipmentIssueStatus.InProgress, ReportedBySubject = Guid.NewGuid() };
        db.AddRange(first, second, new EquipmentIssue { Equipment = items[4], Category = issueCategory, Description = "C", ReportedBySubject = Guid.NewGuid() });
        db.Add(new EquipmentIssue { Equipment = items[2], Category = issueCategory, Description = "Løst", Status = EquipmentIssueStatus.Resolved,
            ResolutionNote = "Repareret", ReportedBySubject = Guid.NewGuid(), ResolvedAt = DateTimeOffset.UtcNow.AddSeconds(1), ResolvedBySubject = Guid.NewGuid() });
        var board = new HardwareDevice { SamdId = Guid.NewGuid().ToString("N"), Equipment = items[1] };
        var passedBoard = new HardwareDevice { SamdId = Guid.NewGuid().ToString("N"), Equipment = items[2] };
        var skipped = new HardwareDevice { SamdId = Guid.NewGuid().ToString("N"), Equipment = items[3] };
        var legacy = new HardwareDevice { SamdId = Guid.NewGuid().ToString("N"), Equipment = items[5] };
        var unlinked = new HardwareDevice { SamdId = Guid.NewGuid().ToString("N") };
        var now = DateTimeOffset.UtcNow;
        db.AddRange(Report(board, now, false, """{"tests":[{"name":"OLED","ok":false},{"name":"SD","ok":false},{"name":"WiFi","ok":false,"skipped":true}]}"""),
            Report(passedBoard, now.AddMinutes(-1), false, """{"tests":[{"name":"OLED","ok":false}]}"""),
            Report(passedBoard, now, true, """{"tests":[{"name":"OLED","ok":true}]}"""),
            Report(skipped, now, true, """{"tests":[{"name":"WiFi","ok":false,"skipped":true}]}"""),
            Report(legacy, now, false, null), Report(unlinked, now, false, null));
        var pending = new LoanRequest { Equipment = items[0], BorrowerSubject = Guid.NewGuid() };
        db.AddRange(pending, new LoanRequest { Equipment = items[3], BorrowerSubject = Guid.NewGuid() },
            new LoanRequest { Equipment = items[4], BorrowerSubject = Guid.NewGuid(), Status = "Rejected" });
        await db.SaveChangesAsync();
        var data = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(6, data.TotalEquipment); Assert.Equal(4, data.AvailableEquipment); Assert.Equal(2, data.LoanedEquipment);
        Assert.Equal(3, data.FaultyEquipment); Assert.Equal(3, data.HardwareFaultCount); Assert.Equal(3, data.ManualFaultCount); Assert.Equal(2, data.PendingRequests);
        var arduino = Assert.Single(data.Categories, c => c.Id == category.Id);
        Assert.Equal((3, 2, 1, 1), (arduino.Total, arduino.Available, arduino.Loaned, arduino.WithFaults));
        var attention = Assert.Single(data.Attention, e => e.Id == items[1].Id);
        Assert.True(attention.IsLoaned); Assert.Equal(2, attention.HardwareFaults); Assert.Equal(2, attention.ManualFaults);
        Assert.Equal(items[1].Id, data.Attention[0].Id);
        var faultFilter = (await client.GetFromJsonAsync<List<EquipmentResponse>>("/api/equipment/?hasFaults=true"))!;
        Assert.Equal(data.FaultyEquipment, faultFilter.Count); Assert.All(faultFilter, e => Assert.True(e.FaultCount > 0));
        Assert.Equal(2, (await client.GetFromJsonAsync<List<EquipmentResponse>>($"/api/equipment/?hasFaults=false&categoryId={category.Id}"))!.Count);
        Assert.Equal(items[1].Id, Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>($"/api/equipment/?hasFaults=true&isLoaned=true&categoryId={category.Id}"))!).Id);
        foreach (var issue in new[] { first, second }) { issue.Status = EquipmentIssueStatus.Resolved; issue.ResolutionNote = "Løst"; issue.ResolvedAt = DateTimeOffset.UtcNow; issue.ResolvedBySubject = Guid.NewGuid(); }
        loans[0].ReturnedAt = DateTimeOffset.UtcNow; loans[0].ReturnedBySubject = Guid.NewGuid();
        db.Add(Report(board, now.AddMinutes(1), true, """{"tests":[{"name":"OLED","ok":true},{"name":"SD","ok":true}]}"""));
        pending.Status = "Approved";
        await db.SaveChangesAsync();
        data = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(2, data.FaultyEquipment); Assert.Equal(1, data.HardwareFaultCount); Assert.Equal(1, data.ManualFaultCount);
        Assert.Equal(1, data.LoanedEquipment); Assert.Equal(5, data.AvailableEquipment); Assert.Equal(1, data.PendingRequests);
        Assert.DoesNotContain(data.Attention, e => e.Id == items[1].Id);
        db.Add(new Loan { Equipment = items[2], BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Guid.NewGuid() });
        await db.SaveChangesAsync();
        Assert.Equal(2, (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!.LoanedEquipment);
    }

    [Fact]
    public async Task Endpoint_requires_manager_or_admin_and_rejects_ordinary_and_anonymous_users()
    {
        await using var app = await StartAsync();
        using var anonymous = app.GetTestClient(); using var user = Client(app, "User"); using var admin = Client(app, "Admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Rendered_dashboard_has_actionable_filters_and_handles_empty_data_errors_and_retry()
    {
        var bridge = new DashboardBridge();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IJSRuntime>(bridge); services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton(new AuthConfiguration("https://auth.mercantec.tech", "test", "http://localhost/auth/callback", "http://localhost/api/"));
        services.AddScoped<MercantecAuthClient>(); services.AddScoped<DashboardClient>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        EquipmentDashboard? component = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DashboardProbe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Capture"] = (Action<EquipmentDashboard>)(c => component = c) })));
        async Task<string> Html() => await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        async Task Refresh() => await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await (Task)typeof(EquipmentDashboard).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
        });
        Assert.Contains("Udstyr i alt", await Html()); Assert.Contains("/udstyr?isLoaned=false", await Html());
        Assert.Contains("/udstyr?hasFaults=true", await Html()); Assert.Contains("Start med det første udstyr", await Html());
        bridge.Fail = true; await Refresh();
        Assert.Contains("Tallene nedenfor er fra sidste vellykkede opdatering", await Html());
        bridge.Fail = false; bridge.WithData = true; await Refresh();
        Assert.Contains("Arduino", await Html()); Assert.Contains("Testboard", await Html()); Assert.Contains("1 fra test · 1 manuelle", await Html());
        Assert.DoesNotContain("Overblikket kunne ikke opdateres", await Html());
    }

    public sealed class DashboardProbe : EquipmentDashboard
    {
        [Parameter] public Action<EquipmentDashboard> Capture { get; set; } = null!;
        protected override async Task OnInitializedAsync() { Capture(this); await base.OnInitializedAsync(); }
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class DashboardBridge : IJSRuntime, IJSObjectReference
    {
        public bool Fail { get; set; }
        public bool WithData { get; set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((T)(object)this);
            var category = Guid.NewGuid(); var id = Guid.NewGuid();
            var data = WithData ? new DashboardResponse(DateTimeOffset.UtcNow, 2, 1, 1, 1, 1, 1, 0,
                [new(category, "Arduino", 2, 1, 1, 1)], [new(id, 100, "Testboard", "Arduino", true, 1, 1)])
                : new DashboardResponse(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0, [], []);
            return ValueTask.FromResult((T)(object)new AuthResponse(Fail ? 503 : 200, JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static HardwareTestReport Report(HardwareDevice device, DateTimeOffset time, bool passed, string? json) => new()
        { Device = device, SamdId = device.SamdId, TestedAt = time, Passed = passed, ReportJson = json };
    private static HttpClient Client(WebApplication app, string role) { var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-Test-Role", role); return client; }
    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<InventoryDbContext>(o => { using var db = postgres.CreateContext(); o.UseNpgsql(db.Database.GetConnectionString()); });
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestUserHandler>("test", _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy("EquipmentManager", p => p.RequireRole("EquipmentManager", "Admin")));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapDashboardEndpoints(); app.MapEquipmentEndpoints(); await app.StartAsync(); return app;
    }
    private sealed class TestUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(role.Length == 0 ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], Scheme.Name)), Scheme.Name)));
        }
    }
}
