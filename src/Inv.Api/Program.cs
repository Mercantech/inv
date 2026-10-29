using Inv.Api.Features.Dashboard;
using Inv.Api.Authentication;
using Inv.Api.Features.Equipment;
using Inv.Api.Health;
using Inv.Infrastructure.Data;
using Inv.Infrastructure.Configuration;
using Inv.Api.Features.EquipmentCategories;
using Inv.Api.Features.Hardware;
using Inv.Api.Features.Loans;
using Inv.Api.Features.Issues;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

LocalDevelopmentEnvironment.Load();
var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
var importOnly = args.Contains("--import-opla", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(argument => argument is not ("--migrate" or "--import-opla")).ToArray());

var connectionString = builder.Configuration["CONNECTION_STRING"]
    ?? builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Databasekonfiguration mangler. Lokalt: opret .env fra .env.example og brug Development-profilen. I Docker/produktion: sæt CONNECTION_STRING eller ConnectionStrings__Default.");
}

builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<HardwareStore>();

if (migrateOnly)
{
    await using var migrationApp = builder.Build();
    await using var scope = migrationApp.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await database.Database.MigrateAsync();
    migrationApp.Logger.LogInformation("Databasemigrationer er gennemført.");
    return;
}

if (importOnly)
{
    await using var importApp = builder.Build();
    await HardwareImportCommand.RunAsync(importApp.Services, builder.Configuration, importApp.Logger);
    return;
}

builder.Services.AddProblemDetails();
builder.Services.AddHttpClient<IBorrowerDirectory, BorrowerDirectory>(client => client.Timeout = TimeSpan.FromSeconds(10))
    .RedactLoggedHeaders(["x-api-key"])
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddOpenApi();
builder.Services.AddMercantecAuthentication(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("hardware-ingestion", _ => RateLimitPartition.GetFixedWindowLimiter("devices", _ =>
        new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var corsOrigins = (builder.Configuration["CORS_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (corsOrigins.Length > 0)
    {
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
    }
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).RequireAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().RequireAuthorization();
}

app.MapGet("/api/me", (System.Security.Claims.ClaimsPrincipal user) => Results.Ok(new
{
    Subject = Guid.Parse(user.FindFirst("sub")!.Value),
    Name = user.FindFirst("name")?.Value,
    Roles = user.FindAll("role")
        .Concat(user.FindAll(System.Security.Claims.ClaimTypes.Role))
        .Select(claim => claim.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray()
})).RequireAuthorization().WithTags("Authentication").WithName("GetCurrentUser");

app.MapGet("/api/manager-access", () => Results.Ok(new { Allowed = true }))
    .RequireAuthorization("EquipmentManager").WithTags("Authentication");

app.MapEquipmentCategoryEndpoints();
app.MapEquipmentEndpoints();
app.MapLoanEndpoints();
app.MapHardwareEndpoints();
app.MapIssueEndpoints();
app.MapDashboardEndpoints();

await app.RunAsync();

public partial class Program;
