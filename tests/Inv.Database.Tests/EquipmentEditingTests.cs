using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Inv.Api.Features.Equipment;
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
    public async Task Shared_save_flow_still_creates_equipment_and_validates_creation()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        await using var db = postgres.CreateContext();
        var category = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        db.Add(category);
        await db.SaveChangesAsync();
        var request = new SaveEquipmentRequest(" Oprettet ", " SERIAL ", category.Id, EquipmentCondition.Good);
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

        var update = new SaveEquipmentRequest("  Nyt navn  ", " NEW ", newCategory.Id, EquipmentCondition.Worn);
        var response = await client.PutAsJsonAsync($"/api/equipment/{equipment.Id}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EquipmentResponse>();
        Assert.NotNull(result);
        Assert.Equal("Nyt navn", result.Name);
        Assert.Equal("NEW", result.SerialNumber);
        Assert.Equal(newCategory.Id, result.EquipmentCategoryId);
        Assert.Equal(EquipmentCondition.Worn, result.Condition);
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
        var valid = new SaveEquipmentRequest("Nyt", "Nyt", equipment.EquipmentCategoryId, EquipmentCondition.Good);
        var path = $"/api/equipment/{equipment.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(path, valid)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        var invalidRequests = new[]
        {
            valid with { Name = " " }, valid with { Name = new string('a', 201) },
            valid with { SerialNumber = null }, valid with { SerialNumber = new string('a', 201) },
            valid with { EquipmentCategoryId = null }, valid with { EquipmentCategoryId = Guid.Empty },
            valid with { EquipmentCategoryId = Guid.NewGuid() },
            valid with { Condition = null }, valid with { Condition = (EquipmentCondition)99 }
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
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapEquipmentEndpoints();
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
