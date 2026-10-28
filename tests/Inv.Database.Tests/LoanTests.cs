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
    public async Task Return_records_server_approval_preserves_history_and_updates_equipment()
    {
        var directory = new TestDirectory();
        await using var app = await StartAsync(directory);
        using var client = Login(app);
        var equipment = await SeedAsync();
        var created = await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new CreateLoanRequest(Borrower, "Oprindelig note"));
        var loan = (await created.Content.ReadFromJsonAsync<LoanResponse>())!;
        directory.Unavailable = true; // Returnering afhænger ikke af Auth-kataloget.
        var before = DateTimeOffset.UtcNow.AddMicroseconds(-1);
        var result = await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans/return", new
        {
            LoanId = loan.Id, ReturnNote = "  Skærm ridset  ", Condition = EquipmentCondition.Damaged,
            ReturnedBySubject = Guid.NewGuid(), ReturnedAt = before.AddYears(-1)
        });
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        await using var db = postgres.CreateContext();
        var saved = await db.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.InRange(saved.ReturnedAt!.Value, before, DateTimeOffset.UtcNow);
        Assert.Equal(Approver, saved.ReturnedBySubject);
        Assert.Equal("Skærm ridset", saved.ReturnNote);
        Assert.Equal("Oprindelig note", saved.Note);
        Assert.Equal(Borrower, saved.BorrowerSubject);
        Assert.Equal(loan.ApprovedBySubject, saved.ApprovedBySubject);
        var reloaded = (await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!;
        Assert.False(reloaded.IsLoaned);
        Assert.Equal(EquipmentCondition.Damaged, reloaded.Condition);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/equipment/{equipment.Id}/loan")).StatusCode);
        Assert.DoesNotContain((await client.GetFromJsonAsync<ActiveLoansResponse>("/api/loans"))!.Loans, l => l.Id == loan.Id);
        directory.Unavailable = false;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new CreateLoanRequest(Borrower, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans/return", new ReturnLoanRequest("Overskriv", loan.Id, EquipmentCondition.Good))).StatusCode);
        await db.Entry(saved).ReloadAsync();
        Assert.Equal("Skærm ridset", saved.ReturnNote);
        Assert.Equal(EquipmentCondition.Damaged, (await db.Equipment.SingleAsync(e => e.Id == equipment.Id)).Condition);
        Assert.True((await client.GetFromJsonAsync<EquipmentResponse>($"/api/equipment/{equipment.Id}"))!.IsLoaned);
        Assert.Equal(2, await db.Loans.CountAsync(l => l.EquipmentId == equipment.Id));
    }

    [Fact]
    public async Task Return_validates_input_and_rejects_unknown_or_inactive_loans()
    {
        await using var app = await StartAsync(new TestDirectory());
        using var client = Login(app);
        var equipment = await SeedAsync();
        var created = await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new CreateLoanRequest(Borrower, null));
        var loan = (await created.Content.ReadFromJsonAsync<LoanResponse>())!;
        var path = $"/api/equipment/{equipment.Id}/loans/return";
        foreach (var request in new[] { new ReturnLoanRequest(null), new ReturnLoanRequest(null, Guid.Empty),
            new ReturnLoanRequest(new string('x', 4001), loan.Id), new ReturnLoanRequest(null, loan.Id, (EquipmentCondition)999) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, new ReturnLoanRequest(null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/equipment/{Guid.NewGuid()}/loans/return", new ReturnLoanRequest(null, loan.Id))).StatusCode);
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, new ReturnLoanRequest(null, loan.Id))).StatusCode);
        Assert.NotNull(await client.GetFromJsonAsync<LoanResponse>($"/api/equipment/{equipment.Id}/loan"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(path, new ReturnLoanRequest(new string('x', 4000), loan.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, new ReturnLoanRequest(null, loan.Id))).StatusCode);
        await using var db = postgres.CreateContext();
        Assert.Equal(4000, (await db.Loans.SingleAsync(l => l.Id == loan.Id)).ReturnNote!.Length);
        Assert.Equal(equipment.Condition, (await db.Equipment.SingleAsync(e => e.Id == equipment.Id)).Condition);
    }

    [Fact]
    public async Task Concurrent_returns_allow_one_winner_and_do_not_overwrite_it()
    {
        await using var app = await StartAsync(new TestDirectory());
        using var client = Login(app);
        var equipment = await SeedAsync();
        var created = await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new CreateLoanRequest(Borrower, null));
        var loan = (await created.Content.ReadFromJsonAsync<LoanResponse>())!;
        var path = $"/api/equipment/{equipment.Id}/loans/return";
        var requests = new[] { new ReturnLoanRequest("A", loan.Id, EquipmentCondition.Good), new ReturnLoanRequest("B", loan.Id, EquipmentCondition.Damaged) };
        var responses = await Task.WhenAll(requests.Select(r => client.PostAsJsonAsync(path, r)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var winner = requests[Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.NoContent)];
        await using var db = postgres.CreateContext();
        Assert.Equal(winner.ReturnNote, (await db.Loans.SingleAsync(l => l.Id == loan.Id)).ReturnNote);
        Assert.Equal(winner.Condition, (await db.Equipment.SingleAsync(e => e.Id == equipment.Id)).Condition);
    }

    [Fact]
    public async Task Active_overview_searches_equipment_and_borrower_and_removes_returned_loans()
    {
        await using var app = await StartAsync(new TestDirectory());
        using var client = Login(app);
        var equipment = await SeedAsync();
        var created = await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans", new CreateLoanRequest(Borrower, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var loan = (await created.Content.ReadFromJsonAsync<LoanResponse>())!;
        foreach (var term in new[] { "  udLÅnsTEST  ", equipment.SerialNumber, "ny auth", "NY@example.test", Borrower.ToString(), $"#{equipment.ShortId}" })
        {
            var result = (await client.GetFromJsonAsync<ActiveLoansResponse>($"/api/loans?search={Uri.EscapeDataString(term)}"))!;
            var row = Assert.Single(result.Loans, l => l.Id == loan.Id);
            Assert.Equal(equipment.Id, row.EquipmentId);
            Assert.Equal(equipment.Name, row.EquipmentName);
            Assert.Equal("Ny Auth-bruger", row.BorrowerName);
            // PostgreSQL gemmer mikrosekunder, mens .NET også har 100 ns-ticks.
            Assert.InRange((loan.LoanedAt - row.LoanedAt).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
            Assert.False(result.DirectoryUnavailable);
        }
        Assert.Empty((await client.GetFromJsonAsync<ActiveLoansResponse>("/api/loans?search=%25_%20ingen"))!.Loans);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync($"/api/equipment/{equipment.Id}/loans/return", new ReturnLoanRequest(null, loan.Id))).StatusCode);
        Assert.DoesNotContain((await client.GetFromJsonAsync<ActiveLoansResponse>("/api/loans"))!.Loans, l => l.Id == loan.Id);
    }

    [Fact]
    public async Task Active_overview_preserves_loans_when_directory_is_unavailable_or_borrower_is_missing()
    {
        var directory = new TestDirectory();
        await using var app = await StartAsync(directory);
        using var client = Login(app);
        var equipment = await SeedAsync();
        await using var db = postgres.CreateContext();
        var loan = new Loan { EquipmentId = equipment.Id, BorrowerSubject = Guid.NewGuid(), ApprovedBySubject = Approver };
        db.Add(loan);
        await db.SaveChangesAsync();
        var result = (await client.GetFromJsonAsync<ActiveLoansResponse>("/api/loans"))!;
        Assert.Null(Assert.Single(result.Loans, l => l.Id == loan.Id).BorrowerName);
        Assert.False(result.DirectoryUnavailable);
        directory.Unavailable = true;
        result = (await client.GetFromJsonAsync<ActiveLoansResponse>($"/api/loans?search={loan.BorrowerSubject}"))!;
        Assert.Equal(loan.Id, Assert.Single(result.Loans).Id);
        Assert.True(result.DirectoryUnavailable);
    }

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
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/loans")).StatusCode);
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
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("EquipmentManager", policy => policy.RequireAuthenticatedUser());
            options.AddPolicy("LoanManager", policy => policy.RequireAuthenticatedUser());
        });
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
