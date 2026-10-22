using Inv.Api.Authentication;
using Inv.Api.Health;
using Inv.Infrastructure.Data;
using Inv.Infrastructure.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

LocalDevelopmentEnvironment.Load();
var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--migrate").ToArray());

var connectionString = builder.Configuration["CONNECTION_STRING"]
    ?? builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Databasekonfiguration mangler. Lokalt: opret .env fra .env.example og brug Development-profilen. I Docker/produktion: sæt CONNECTION_STRING eller ConnectionStrings__Default.");
}

builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(connectionString));

if (migrateOnly)
{
    await using var migrationApp = builder.Build();
    await using var scope = migrationApp.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await database.Database.MigrateAsync();
    migrationApp.Logger.LogInformation("Databasemigrationer er gennemført.");
    return;
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddMercantecAuthentication(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

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
}).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/api/me", (System.Security.Claims.ClaimsPrincipal user) => Results.Ok(new
{
    Subject = Guid.Parse(user.FindFirst("sub")!.Value),
    Name = user.FindFirst("name")?.Value,
    Roles = user.FindAll("role").Select(claim => claim.Value).ToArray()
})).RequireAuthorization().WithTags("Authentication").WithName("GetCurrentUser");

await app.RunAsync();

public partial class Program;
