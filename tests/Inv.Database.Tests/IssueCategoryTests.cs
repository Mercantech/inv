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

public sealed class IssueCategoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Category_list_filters_by_current_equipment_type_and_keeps_admin_list_complete()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        await using var db = postgres.CreateContext();
        var a = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var b = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var equipment = new Equipment { Name = "Board", SerialNumber = "TEST", Category = a };
        var global = new IssueCategory { Name = Guid.NewGuid().ToString() };
        var matching = new IssueCategory { Name = Guid.NewGuid().ToString(), EquipmentCategory = a };
        var other = new IssueCategory { Name = Guid.NewGuid().ToString(), EquipmentCategory = b };
        db.AddRange(equipment, global, matching, other);
        await db.SaveChangesAsync();
        var path = $"/api/issue-categories/?equipmentId={equipment.Id}";
        var result = (await client.GetFromJsonAsync<List<IssueCategoryResponse>>(path))!;
        Assert.Contains(result, c => c.Id == global.Id);
        Assert.Contains(result, c => c.Id == matching.Id);
        Assert.DoesNotContain(result, c => c.Id == other.Id);
        Assert.Contains((await client.GetFromJsonAsync<List<IssueCategoryResponse>>("/api/issue-categories/"))!, c => c.Id == other.Id);
        equipment.EquipmentCategoryId = b.Id;
        await db.SaveChangesAsync();
        result = (await client.GetFromJsonAsync<List<IssueCategoryResponse>>(path))!;
        Assert.Contains(result, c => c.Id == other.Id);
        Assert.DoesNotContain(result, c => c.Id == matching.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/issue-categories/?equipmentId={Guid.NewGuid()}")).StatusCode);
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task New_and_changed_issue_categories_must_match_but_old_reports_remain_actionable()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        await using var db = postgres.CreateContext();
        var a = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var b = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var equipment = new Equipment { Name = "Board", SerialNumber = "TEST", Category = a };
        var global = new IssueCategory { Name = Guid.NewGuid().ToString() };
        var matching = new IssueCategory { Name = Guid.NewGuid().ToString(), EquipmentCategory = a };
        var other = new IssueCategory { Name = Guid.NewGuid().ToString(), EquipmentCategory = b };
        db.AddRange(equipment, global, matching, other);
        await db.SaveChangesAsync();
        var path = $"/api/equipment/{equipment.Id}/issues";
        foreach (var categoryId in new[] { other.Id, Guid.NewGuid(), Guid.Empty })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new CreateIssueRequest(categoryId, "Fejl"))).StatusCode);
        Assert.False(await db.EquipmentIssues.AnyAsync(i => i.EquipmentId == equipment.Id));
        var created = await client.PostAsJsonAsync(path, new CreateIssueRequest(matching.Id, "Original"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var issue = (await created.Content.ReadFromJsonAsync<EquipmentIssueResponse>())!;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(path, new CreateIssueRequest(global.Id, "Ekstra"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + $"/{issue.Id}", new UpdateIssueRequest(other.Id, "Må ikke gemmes", "InProgress", null))).StatusCode);
        var saved = await db.EquipmentIssues.AsNoTracking().SingleAsync(i => i.Id == issue.Id);
        Assert.Equal("Original", saved.Description);
        Assert.Equal(matching.Id, saved.IssueCategoryId);
        Assert.Equal(EquipmentIssueStatus.Open, saved.Status);
        equipment.EquipmentCategoryId = b.Id;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new CreateIssueRequest(matching.Id, "Gammel formular"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path + $"/{issue.Id}", new UpdateIssueRequest(matching.Id, null, "Resolved", "Repareret"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path + $"/{issue.Id}", new UpdateIssueRequest(other.Id, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task Category_names_validate_and_duplicates_are_scoped_and_used_categories_cannot_be_misassigned()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        await using var db = postgres.CreateContext();
        var a = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        var b = new EquipmentCategory { Name = Guid.NewGuid().ToString() };
        db.AddRange(a, b);
        await db.SaveChangesAsync();
        const string path = "/api/issue-categories/";
        foreach (var request in new[] { new SaveIssueCategoryRequest(" ", null), new SaveIssueCategoryRequest(new string('x', 151), null), new SaveIssueCategoryRequest("Ukendt", Guid.NewGuid()) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, request)).StatusCode);
        var name = Guid.NewGuid().ToString();
        var created = await client.PostAsJsonAsync(path, new SaveIssueCategoryRequest(name, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var global = (await created.Content.ReadFromJsonAsync<IssueCategoryResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, new SaveIssueCategoryRequest(" " + name.ToUpperInvariant() + " ", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(path, new SaveIssueCategoryRequest(name, a.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, new SaveIssueCategoryRequest(name, a.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(path, new SaveIssueCategoryRequest(name, b.Id))).StatusCode);
        var equipment = new Equipment { Name = "Board", SerialNumber = "TEST", Category = a };
        db.Add(equipment);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/issues", new CreateIssueRequest(global.Id, "Fejl"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path + global.Id, new SaveIssueCategoryRequest(name, b.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path + global.Id, new SaveIssueCategoryRequest(name + " rettet", null))).StatusCode);
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
