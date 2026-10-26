using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Inv.Api.Features.Equipment;
using Inv.Api.Features.Loans;
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

public sealed class LoanTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly Guid Borrower = Guid.NewGuid();
    private static readonly Guid Disabled = Guid.NewGuid();

    [Fact]
    public async Task Loan_uses_auth_subject_and_server_approval_and_becomes_visible_after_reload()
    {
        await using var app = await StartAsync(new TestDirectory());
        using var client = Login(app);
        var equipment = await SeedAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/equipment/{equipment.Id}/loan")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.IsLoaned);
        // Låneren findes kun i Auth-kataloget og har ingen lokal konto eller tidligere udlån.
        var borrowers = await client.GetFromJsonAsync<List<BorrowerResponse>>("/api/borrowers?search=ny%40example.test");
        Assert.Equal(Borrower, Assert.Single(borrowers!).Subject);
        var before = DateTimeOffset.UtcNow;
        var response = await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new
        {
            BorrowerSubject = Borrower, Note = "  Første udlån  ",
            ApprovedBySubject = Guid.NewGuid(), LoanedAt = before.AddYears(-1)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var loan = await response.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.NotNull(loan);
        Assert.Equal(Borrower, loan.BorrowerSubject);
        Assert.Equal(Approver, loan.ApprovedBySubject);
        Assert.InRange(loan.LoanedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal("Første udlån", loan.Note);
        var reloaded = await client.GetFromJsonAsync<LoanResponse>(response.Headers.Location);
        Assert.Equal(loan.Id, reloaded!.Id);
        Assert.Equal(loan.Note, reloaded.Note);
        Assert.True((await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.IsLoaned);
        Assert.True((await client.GetFromJsonAsync<List<EquipmentResponse>>("/api/equipment/"))!
            .Single(e => e.Id == equipment.Id).IsLoaned);
        // Redigering må ikke få et aktivt udlån til at fremstå ledigt.
        var edited = await client.PutAsJsonAsync($"/api/equipment/{equipment.Id}",
            new SaveEquipmentRequest("Rettet", "SERIAL", equipment.EquipmentCategoryId, EquipmentCondition.Good));
        Assert.True((await edited.Content.ReadFromJsonAsync<EquipmentResponse>())!.IsLoaned);
    }

    [Fact]
    public async Task Invalid_borrowers_notes_and_equipment_are_rejected_without_creating_loans()
    {
        await using var app = await StartAsync(new TestDirectory());
        using var client = Login(app);
        var equipment = await SeedAsync();
        var requests = new[]
        {
            new CreateLoanRequest(null, null), new CreateLoanRequest(Guid.Empty, null),
            new CreateLoanRequest(Guid.NewGuid(), null), new CreateLoanRequest(Disabled, null),
            new CreateLoanRequest(Borrower, new string('x', 4001))
        };
        foreach (var request in requests)
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/equipment/{Guid.NewGuid()}/loans", new CreateLoanRequest(Borrower, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/equipment/{Guid.NewGuid()}/loan")).StatusCode);
        await using var db = postgres.CreateContext();
        Assert.False(await db.Loans.AnyAsync(l => l.EquipmentId == equipment.Id));
    }

    [Fact]
    public async Task All_loan_endpoints_require_login()
    {
        await using var app = await StartAsync(new TestDirectory());
        using var client = app.GetTestClient();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/borrowers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/equipment/{id}/loan")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/api/equipment/{id}/loans", new CreateLoanRequest(Borrower, null))).StatusCode);
    }

    [Fact]
    public async Task Directory_failure_prevents_creation_but_does_not_hide_existing_loan_status()
    {
        var directory = new TestDirectory { Unavailable = true };
        await using var app = await StartAsync(directory);
        using var client = Login(app);
        var equipment = await SeedAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/borrowers")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new CreateLoanRequest(Borrower, null))).StatusCode);
        await using var db = postgres.CreateContext();
        Assert.False(await db.Loans.AnyAsync(l => l.EquipmentId == equipment.Id));
        directory.Unavailable = false;
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new CreateLoanRequest(Borrower, null))).StatusCode);
        directory.Unavailable = true;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/equipment/{equipment.Id}/loan")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new CreateLoanRequest(Borrower, null))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_loans_allow_one_creation_and_return_conflict_for_the_other()
    {
        await using var app = await StartAsync(new TestDirectory { WaitForTwoCalls = true });
        using var client = Login(app);
        var equipment = await SeedAsync();
        var path = $"/api/equipment/{equipment.Id}/loans";
        var results = await Task.WhenAll(
            client.PostAsJsonAsync(path, new CreateLoanRequest(Borrower, "A")),
            client.PostAsJsonAsync(path, new CreateLoanRequest(Borrower, "B")));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var db = postgres.CreateContext();
        Assert.Equal(1, await db.Loans.CountAsync(l => l.EquipmentId == equipment.Id && l.ReturnedAt == null));
    }

    [Fact]
    public async Task A_new_loan_preserves_returned_history_and_accepts_the_maximum_note_length()
    {
        var equipment = await SeedAsync();
        await using var db = postgres.CreateContext();
        var previous = new Loan
        {
            EquipmentId = equipment.Id, BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Approver,
            LoanedAt = DateTimeOffset.UtcNow.AddDays(-2), ReturnedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ReturnedBySubject = Approver, Note = "Bevar note", ReturnNote = "Bevar returnote"
        };
        db.Add(previous);
        await db.SaveChangesAsync();
        await using var app = await StartAsync(new TestDirectory());
        using var client = Login(app);
        var response = await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans",
            new CreateLoanRequest(Borrower, new string('x', 4000)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(4000, (await response.Content.ReadFromJsonAsync<LoanResponse>())!.Note!.Length);
        await db.Entry(previous).ReloadAsync();
        Assert.Equal("Bevar note", previous.Note);
        Assert.Equal("Bevar returnote", previous.ReturnNote);
        Assert.NotNull(previous.ReturnedAt);
        Assert.Equal(2, await db.Loans.CountAsync(l => l.EquipmentId == equipment.Id));
    }

    private async Task<Equipment> SeedAsync()
    {
        await using var db = postgres.CreateContext();
        var equipment = new Equipment
        {
            Name = "Udlånstest", SerialNumber = Guid.NewGuid().ToString(),
            Category = new EquipmentCategory { Name = Guid.NewGuid().ToString() }
        };
        db.Add(equipment);
        await db.SaveChangesAsync();
        return equipment;
    }

    private static HttpClient Login(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        return client;
    }

    private async Task<WebApplication> StartAsync(IBorrowerDirectory directory)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<InventoryDbContext>(options =>
        {
            using var db = postgres.CreateContext();
            options.UseNpgsql(db.Database.GetConnectionString());
        });
        builder.Services.AddSingleton(directory);
        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, TestUserHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapEquipmentEndpoints();
        app.MapLoanEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestDirectory : IBorrowerDirectory
    {
        public bool Unavailable { get; set; }
        public bool WaitForTwoCalls { get; init; }
        private int calls;
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<BorrowerResponse>> GetAsync(CancellationToken ct)
        {
            if (Unavailable) throw new DirectoryUnavailableException();
            if (WaitForTwoCalls)
            {
                if (Interlocked.Increment(ref calls) == 2) both.TrySetResult();
                await both.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            return [new(Borrower, "Ny Auth-bruger", "ny@example.test", false), new(Disabled, "Lukket konto", null, true)];
        }
    }

    private sealed class TestUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Test-User")
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim("sub", Approver.ToString())], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }
}
