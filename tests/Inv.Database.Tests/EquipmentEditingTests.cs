using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Inv.Api.Features.Equipment;
using Inv.Api.Features.Issues;
using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Inv.Database.Tests;

public sealed class EquipmentEditingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Loan_filter_combines_with_other_filters_and_tracks_return_and_reloan()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        await using var db = postgres.CreateContext();
        var category = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var available = new Equipment { Name = "Filtertest ledig", SerialNumber = "SER-LEDIG", Category = category };
        var returned = new Equipment { Name = "Filtertest returneret", SerialNumber = "SER-RETUR", Category = category };
        var borrowed = new Equipment { Name = "Filtertest udlånt", SerialNumber = "SER-UD", Category = category };
        var historical = new Loan { Equipment = returned, BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Guid.NewGuid(),
            LoanedAt = DateTimeOffset.UtcNow.AddDays(-2), ReturnedAt = DateTimeOffset.UtcNow.AddDays(-1), ReturnedBySubject = Guid.NewGuid() };
        var active = new Loan { Equipment = borrowed, BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Guid.NewGuid() };
        db.AddRange(available, historical, active);
        await db.SaveChangesAsync();
        var path = $"/api/equipment/?categoryId={category.Id}";
        Assert.Equal(3, (await client.GetFromJsonAsync<List<EquipmentResponse>>(path))!.Count);
        var onlyBorrowed = Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&isLoaned=true"))!);
        Assert.Equal(borrowed.Id, onlyBorrowed.Id);
        Assert.True(onlyBorrowed.IsLoaned);
        var free = (await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&isLoaned=false"))!;
        Assert.Equal(2, free.Count);
        Assert.All(free, item => Assert.False(item.IsLoaned));
        Assert.Contains(free, item => item.Id == returned.Id);
        Assert.Equal(available.Id, Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&isLoaned=false&search=SER-LEDIG"))!).Id);
        Assert.Equal(returned.Id, Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&isLoaned=false&search=SER-RETUR"))!).Id);
        Assert.Empty((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&isLoaned=true&search=SER-RETUR"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "&isLoaned=invalid")).StatusCode);
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path + "&isLoaned=true")).StatusCode);
        active.ReturnedAt = DateTimeOffset.UtcNow;
        active.ReturnedBySubject = Guid.NewGuid();
        await db.SaveChangesAsync();
        Assert.Empty((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&isLoaned=true"))!);
        Assert.Equal(3, (await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&isLoaned=false"))!.Count);
        db.Loans.Add(new Loan { EquipmentId = borrowed.Id, BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Guid.NewGuid() });
        await db.SaveChangesAsync();
        Assert.Equal(borrowed.Id, Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&isLoaned=true"))!).Id);
    }

    [Fact]
    public async Task Shared_save_flow_still_creates_equipment_and_validates_creation()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        await using var db = postgres.CreateContext();
        var category = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        db.Add(category);
        await db.SaveChangesAsync();
        var request = new SaveEquipmentRequest(" Oprettet ", " SERIAL ", category.Id);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/equipment/", request with { Name = " " })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/equipment/", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EquipmentResponse>();
        Assert.NotNull(result);
        Assert.Equal("Oprettet", result.Name);
        Assert.Equal("SERIAL", result.SerialNumber);
        Assert.True(result.ShortId >= 100);
        Assert.Equal($"/api/equipment/{result.Id}", response.Headers.Location!.ToString());
        AssertPersisted(result, (await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{result.Id}"))!);
    }

    [Fact]
    public async Task Editing_preserves_identity_creation_time_and_loan_history()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        await using var db = postgres.CreateContext();
        var originalCategory = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var newCategory = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var equipment = new Equipment { Name = "Oprindeligt", SerialNumber = "OLD", Category = originalCategory };
        var loan = new Loan
        {
            Equipment = equipment,
            BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Guid.NewGuid(),
            LoanedAt = DateTimeOffset.UtcNow.AddDays(-2), ReturnedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ReturnedBySubject = Guid.NewGuid(), Note = "Historisk note", ReturnNote = "Returneret"
        };
        db.AddRange(loan, newCategory);
        await db.SaveChangesAsync();
        await db.Entry(equipment).ReloadAsync();
        await db.Entry(loan).ReloadAsync();
        var before = (equipment.Id, equipment.ShortId, equipment.CreatedAt, equipment.UpdatedAt);
        var loanBefore = System.Text.Json.JsonSerializer.Serialize(new
        {
            loan.Id, loan.EquipmentId, loan.BorrowerSubject, loan.ApprovedBySubject,
            loan.LoanedAt, loan.ReturnedAt, loan.ReturnedBySubject, loan.Note, loan.ReturnNote,
            loan.CreatedAt, loan.UpdatedAt
        });

        var update = new SaveEquipmentRequest("  Nyt navn  ", " NEW ", newCategory.Id);
        var response = await client.PutAsJsonAsync($"/api/equipment/{equipment.Id}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EquipmentResponse>();
        Assert.NotNull(result);
        Assert.Equal("Nyt navn", result.Name);
        Assert.Equal("NEW", result.SerialNumber);
        Assert.Equal(newCategory.Id, result.EquipmentCategoryId);
        Assert.Equal(before.Id, result.Id);
        Assert.Equal(before.ShortId, result.ShortId);
        Assert.Equal(before.CreatedAt, result.CreatedAt);
        Assert.True(result.UpdatedAt > before.UpdatedAt);
        AssertPersisted(result, (await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!);

        await db.Entry(loan).ReloadAsync();
        Assert.Equal(loanBefore, System.Text.Json.JsonSerializer.Serialize(new
        {
            loan.Id, loan.EquipmentId, loan.BorrowerSubject, loan.ApprovedBySubject,
            loan.LoanedAt, loan.ReturnedAt, loan.ReturnedBySubject, loan.Note, loan.ReturnNote,
            loan.CreatedAt, loan.UpdatedAt
        }));

        // Gem uden nye feltændringer skal stadig opdatere ændringstidspunktet.
        var savedAgain = await client.PutAsJsonAsync($"/api/equipment/{equipment.Id}", update);
        Assert.Equal(HttpStatusCode.OK, savedAgain.StatusCode);
        Assert.True((await savedAgain.Content.ReadFromJsonAsync<EquipmentResponse>())!.UpdatedAt > result.UpdatedAt);
    }

    [Fact]
    public async Task Editing_requires_login_rejects_invalid_fields_and_does_not_save_invalid_changes()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await using var db = postgres.CreateContext();
        var equipment = new Equipment
        {
            Name = "Bevar", SerialNumber = "Bevar",
            Category = new EquipmentCategory { Name = Guid.NewGuid().ToString() }
        };
        db.Add(equipment);
        await db.SaveChangesAsync();
        await db.Entry(equipment).ReloadAsync();
        var updatedAt = equipment.UpdatedAt;
        var valid = new SaveEquipmentRequest("Nyt", "Nyt", equipment.EquipmentCategoryId);
        var path = $"/api/equipment/{equipment.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(path, valid)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        var invalidRequests = new[]
        {
            valid with { Name = " " }, valid with { Name = new string('a', 201) },
            valid with { SerialNumber = null }, valid with { SerialNumber = new string('a', 201) },
            valid with { EquipmentCategoryId = null }, valid with { EquipmentCategoryId = Guid.Empty },
            valid with { EquipmentCategoryId = Guid.NewGuid() },
        };
        foreach (var invalid in invalidRequests)
        {
            var response = await client.PutAsJsonAsync(path, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("errors", await response.Content.ReadAsStringAsync());
        }
        await db.Entry(equipment).ReloadAsync();
        Assert.Equal("Bevar", equipment.Name);
        Assert.Equal("Bevar", equipment.SerialNumber);
        Assert.Equal(updatedAt, equipment.UpdatedAt);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/equipment/{Guid.NewGuid()}", valid)).StatusCode);
    }

    [Fact]
    public async Task Fault_filters_follow_latest_board_test_and_keep_manual_issues_independent()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        await using var db = postgres.CreateContext();
        var category = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var equipment = new Equipment { Name = "Fejltest", SerialNumber = Guid.NewGuid().ToString(), Category = category };
        var manual = new IssueCategory { Name = Guid.NewGuid().ToString() };
        var board = new HardwareDevice { SamdId = Guid.NewGuid().ToString("N"), Equipment = equipment };
        var report = new HardwareTestReport { Device = board, SamdId = board.SamdId, Passed = false,
            FailedCount = 1, ReportJson = """{"tests":[{"name":"OLED","ok":false,"note":"Sort skærm"},{"name":"WiFi","ok":false,"skipped":true}]}""" };
        db.AddRange(equipment, manual, report);
        await db.SaveChangesAsync();
        var path = $"/api/equipment/?categoryId={category.Id}";
        Assert.Equal(1, (await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.FaultCount);
        Assert.Equal(1, Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&faultCount=1"))!).FaultCount);
        Assert.Empty((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&faultCount=0"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "&faultCount=-1")).StatusCode);
        Assert.Equal(equipment.Id, Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&fault=hardware:OLED"))!).Id);
        Assert.Empty((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&fault=hardware:WIFI"))!);
        Assert.Empty((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&fault=hardware:OLED&search=nomatch"))!);
        var faults = (await client.GetFromJsonAsync<List<HardwareFaultResponse>>($"/api/equipment/{equipment.Id}/hardware-faults"))!;
        Assert.Equal("OLED", Assert.Single(faults).Name);
        var categories = (await client.GetFromJsonAsync<List<FaultCategoryResponse>>("/api/fault-categories"))!;
        Assert.Single(categories, c => c.Key == "hardware:OLED");
        Assert.Contains(categories, c => c.Key == $"manual:{manual.Id}");
        var created = await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/issues", new CreateIssueRequest(manual.Id, "Kabinettet er revnet"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var issue = (await created.Content.ReadFromJsonAsync<EquipmentIssueResponse>())!;
        Assert.Equal(2, (await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.FaultCount);
        Assert.Equal(2, Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&faultCount=2&fault=hardware:OLED"))!).FaultCount);
        db.HardwareTestReports.Add(new HardwareTestReport { Device = board, SamdId = board.SamdId,
            TestedAt = report.TestedAt.AddMinutes(1), Passed = true, PassedCount = 1,
            ReportJson = """{"tests":[{"name":"oled","ok":true}]}""" });
        await db.SaveChangesAsync();
        Assert.Empty((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&fault=hardware:OLED"))!);
        Assert.Equal(1, (await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.FaultCount);
        Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + $"&fault=manual:{manual.Id}"))!);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/equipment/{equipment.Id}/issues/{issue.Id}",
            new UpdateIssueRequest(null, null, "Resolved", "Repareret"))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + $"&fault=manual:{manual.Id}"))!);
        Assert.Equal(0, (await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.FaultCount);
        Assert.Equal(0, Assert.Single((await client.GetFromJsonAsync<List<EquipmentResponse>>(path + "&faultCount=0"))!).FaultCount);
        Assert.Equal(2, await db.HardwareTestReports.CountAsync(r => r.SamdId == board.SamdId));
        Assert.Single((await client.GetFromJsonAsync<List<FaultCategoryResponse>>("/api/fault-categories"))!, c => c.Key == "hardware:OLED");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "&fault=bad")).StatusCode);
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/fault-categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/equipment/{equipment.Id}/hardware-faults")).StatusCode);
    }

    private static void AssertPersisted(EquipmentResponse expected, EquipmentResponse actual)
    {
        // PostgreSQL gemmer mikrosekunder, mens .NET også har 100 ns-ticks.
        Assert.InRange(Math.Abs((expected.CreatedAt - actual.CreatedAt).Ticks), 0L, 9L);
        Assert.InRange(Math.Abs((expected.UpdatedAt - actual.UpdatedAt).Ticks), 0L, 9L);
        Assert.Equal(expected with { CreatedAt = actual.CreatedAt, UpdatedAt = actual.UpdatedAt }, actual);
    }

    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<InventoryDbContext>(options =>
        {
            using var db = postgres.CreateContext();
            options.UseNpgsql(db.Database.GetConnectionString());
        });
        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, TestUserHandler>("test", _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("EquipmentManager", policy => policy.RequireAuthenticatedUser()));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapEquipmentEndpoints();
        app.MapIssueEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Test-User")
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }
}
