using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using Inv.Api.Features.Hardware;
using Inv.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Inv.Database.Tests;

public sealed class HardwareTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Import_preserves_timestamps_raw_details_and_links_and_is_repeatable()
    {
        await using var db = postgres.CreateContext();
        var store = new HardwareStore(db);
        var samd = NewSamd();
        var when = DateTimeOffset.Parse("2025-03-04T10:00:00Z");
        var legacy = new LegacyDevice(samd, "AA:BB", "ECC", "Oplà", when, when,
            [new(1001, samd, false, 1, 1, "original text", Report(false), when)]);
        var first = await store.ImportAsync([legacy], default);
        Assert.Equal(new ImportResult(1, 1, 0), first);
        var category = new EquipmentCategory { Name = NewSamd() };
        var equipment = new Equipment { Name = "Eksisterende", SerialNumber = samd, Category = category };
        db.Equipment.Add(equipment);
        var device = await db.HardwareDevices.SingleAsync(device => device.SamdId == samd);
        device.Equipment = equipment;
        await db.SaveChangesAsync();
        var second = await store.ImportAsync([legacy with { Label = "Gammelt navn" }], default);
        Assert.Equal(new ImportResult(0, 0, 1), second);
        db.ChangeTracker.Clear();
        device = await db.HardwareDevices.SingleAsync(device => device.SamdId == samd);
        Assert.Equal(equipment.Id, device.EquipmentId);
        Assert.Equal("Oplà", device.Label);
        Assert.Equal(when, device.RegisteredAt);
        var test = await db.HardwareTestReports.SingleAsync(report => report.SamdId == samd);
        Assert.Equal(when, test.TestedAt);
        Assert.Equal("original text", test.Details);
        Assert.Equal("arduino-opla", test.ImportSource);
        Assert.Equal("OLED", HardwareContracts.Result(test).Components[0].Name);
    }

    [Fact]
    public async Task Invalid_import_is_atomic_and_cannot_move_a_report_to_another_board()
    {
        await using var db = postgres.CreateContext();
        var samd = NewSamd();
        var when = DateTimeOffset.UtcNow;
        var legacy = new LegacyDevice(samd, null, null, null, when, when,
            [new(1002, NewSamd(), true, 1, 0, null, Report(true), when)]);
        await Assert.ThrowsAsync<ArgumentException>(() => new HardwareStore(db).ImportAsync([legacy], default));
        Assert.False(await db.HardwareDevices.AnyAsync(device => device.SamdId == samd));
    }

    [Fact]
    public async Task Hardware_endpoints_enforce_user_access_and_validate_reports()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var samd = NewSamd();
        var registration = new { samd_id = samd, label = "Testboard" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/hardware/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/devices", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/hardware/devices")).StatusCode);
        var invalid = new { passed = true, passed_count = 2, failed_count = 0, report = Report(false) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/devices/{samd}/tests", invalid)).StatusCode);
        var valid = new { passed = false, passed_count = 1, failed_count = 1, report = Report(false) };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/devices/{samd}/tests", valid)).StatusCode);
        var passed = new { passed = true, passed_count = 2, failed_count = 0, report = Report(true) };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/devices/{samd}/tests", passed)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        var tests = await client.GetFromJsonAsync<List<TestResult>>($"/api/hardware/devices/{samd}/tests");
        Assert.Equal(2, tests!.Count);
        Assert.True(tests[0].Passed);
        Assert.False(tests[1].Passed);
        Assert.True(Assert.Single(tests[0].Components, component => component.Name == "Buzzer").Skipped);
        var devices = await client.GetFromJsonAsync<List<DeviceResult>>("/api/hardware/devices");
        Assert.True(Assert.Single(devices!, device => device.SamdId == samd).LatestTest!.Passed);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/hardware/devices/{samd}/tests?take=101")).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<TestResult>>($"/api/hardware/devices/{samd}/tests?take=1&skip=1"))!);
    }

    [Fact]
    public async Task Concurrent_registration_and_equipment_creation_do_not_duplicate_rows()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        var samd = NewSamd();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/devices", new { samd_id = samd })));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        var created = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync($"/api/hardware/devices/{samd}/create-equipment", new { })));
        Assert.All(created, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        await using var db = postgres.CreateContext();
        var device = await db.HardwareDevices.SingleAsync(device => device.SamdId == samd);
        Assert.NotNull(device.EquipmentId);
        Assert.Single(await db.Equipment.Where(equipment => equipment.SerialNumber == samd).ToListAsync());
        var other = NewSamd();
        await client.PostAsJsonAsync("/api/devices", new { samd_id = other });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/hardware/devices/{other}/equipment", new { device.EquipmentId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/hardware/devices/{samd}/equipment", new { equipmentId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/hardware/devices/{other}/equipment", new { device.EquipmentId })).StatusCode);
    }

    [Fact]
    public async Task Authenticated_hardware_submit_registers_device_and_saves_test()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var samd = NewSamd();
        var registration = new { samd_id = samd, mac = "AA:BB:CC:DD:EE:FF", label = "Web Serial" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/hardware/devices", registration)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/hardware/devices", registration)).StatusCode);
        var valid = new { passed = true, passed_count = 2, failed_count = 0, report = Report(true) };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/hardware/devices/{samd}/tests", valid)).StatusCode);
        var tests = await client.GetFromJsonAsync<List<TestResult>>($"/api/hardware/devices/{samd}/tests");
        Assert.True(Assert.Single(tests!).Passed);
    }

    [Fact]
    public async Task Short_ids_are_unique_stable_and_searchable()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        var samd = NewSamd();
        await client.PostAsJsonAsync("/api/devices", new { samd_id = samd });
        await using var db = postgres.CreateContext();
        var board = await db.HardwareDevices.SingleAsync(item => item.SamdId == samd);
        var shortId = board.ShortId;
        Assert.True(shortId >= 100);
        await client.PostAsJsonAsync("/api/devices", new { samd_id = samd, label = "Updated" });
        await client.PostAsJsonAsync($"/api/devices/{samd}/tests", new { passed = true, passed_count = 2, failed_count = 0, report = Report(true) });
        await db.Entry(board).ReloadAsync();
        Assert.Equal(shortId, board.ShortId);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/hardware/devices/{samd}/create-equipment", new { })).StatusCode);
        await db.Entry(board).ReloadAsync();
        var equipment = await db.Equipment.SingleAsync(item => item.Id == board.EquipmentId);
        Assert.True(equipment.ShortId >= 100);
        Assert.NotEqual(shortId, equipment.ShortId);
        foreach (var term in new[] { equipment.ShortId.ToString(), $"#{equipment.ShortId}" })
        {
            var results = await client.GetFromJsonAsync<List<Inv.Api.Features.Equipment.EquipmentResponse>>($"/api/equipment/?search={Uri.EscapeDataString(term)}");
            Assert.Equal(equipment.Id, Assert.Single(results!).Id);
        }
        var boards = await client.GetFromJsonAsync<List<DeviceResult>>("/api/hardware/devices");
        var result = Assert.Single(boards!, item => item.SamdId == samd);
        Assert.Equal(shortId, result.ShortId);
        Assert.Equal(equipment.ShortId, result.EquipmentShortId);
    }

    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<Inv.Infrastructure.Data.InventoryDbContext>(options =>
        {
            using var db = postgres.CreateContext();
            options.UseNpgsql(db.Database.GetConnectionString());
        });
        builder.Services.AddScoped<HardwareStore>();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestUserHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy("hardware-ingestion", _ =>
            RateLimitPartition.GetNoLimiter("test")));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapHardwareEndpoints();
        Inv.Api.Features.Equipment.EquipmentEndpoints.MapEquipmentEndpoints(app);
        await app.StartAsync();
        return app;
    }

    private static string NewSamd() => Guid.NewGuid().ToString("N").ToUpperInvariant();
    private static JsonElement Report(bool passed) => JsonSerializer.SerializeToElement(new
    {
        firmware = "HardwareTest",
        tests = new[]
        {
            new { name = "OLED", ok = passed, skipped = false, note = "screen" },
            new { name = "SD", ok = true, skipped = false, note = "rw=ok" },
            new { name = "Buzzer", ok = true, skipped = true, note = "quiet" }
        }
    });

    private sealed class TestUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Test-User")
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }
}
