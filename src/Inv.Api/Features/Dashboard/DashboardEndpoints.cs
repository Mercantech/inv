using System.Data;
using Inv.Api.Features.Issues;
using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inv.Api.Features.Dashboard;

public sealed record DashboardCategory(Guid Id, string Name, int Total, int Loaned, int Available, int WithFaults);
public sealed record DashboardEquipment(Guid Id, int ShortId, string Name, string CategoryName, bool IsLoaned, int HardwareFaults, int ManualFaults);
public sealed record DashboardResponse(DateTimeOffset GeneratedAt, int TotalEquipment, int AvailableEquipment,
    int LoanedEquipment, int FaultyEquipment, int HardwareFaultCount, int ManualFaultCount, int PendingRequests,
    IReadOnlyList<DashboardCategory> Categories, IReadOnlyList<DashboardEquipment> Attention);

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard", async (InventoryDbContext db, CancellationToken ct) =>
        {
            // All figures describe the same database snapshot, even during loans or retests.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var equipment = await db.Equipment.AsNoTracking().Select(e => new
            {
                e.Id, e.ShortId, e.Name, CategoryId = e.EquipmentCategoryId, CategoryName = e.Category.Name,
                IsLoaned = e.Loans.Any(l => l.ReturnedAt == null)
            }).ToListAsync(ct);
            var hardware = (await HardwareFaults.CurrentAsync(db, null, ct)).GroupBy(f => f.EquipmentId).ToDictionary(g => g.Key, g => g.Count());
            var manual = await db.EquipmentIssues.AsNoTracking().Where(i => i.Status != EquipmentIssueStatus.Resolved)
                .GroupBy(i => i.EquipmentId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
            bool HasFaults(Guid id) => hardware.GetValueOrDefault(id) + manual.GetValueOrDefault(id) > 0;
            var loaned = equipment.Count(e => e.IsLoaned);
            var pending = await db.LoanRequests.CountAsync(r => r.Status == "Pending", ct);
            var categories = equipment.GroupBy(e => new { e.CategoryId, e.CategoryName }).Select(g => new DashboardCategory(
                g.Key.CategoryId, g.Key.CategoryName, g.Count(), g.Count(e => e.IsLoaned), g.Count(e => !e.IsLoaned), g.Count(e => HasFaults(e.Id))))
                .OrderByDescending(c => c.Total).ThenBy(c => c.Name).ToList();
            var attention = equipment.Where(e => HasFaults(e.Id)).Select(e => new DashboardEquipment(e.Id, e.ShortId, e.Name,
                e.CategoryName, e.IsLoaned, hardware.GetValueOrDefault(e.Id), manual.GetValueOrDefault(e.Id)))
                .OrderByDescending(e => e.HardwareFaults + e.ManualFaults).ThenBy(e => e.Name).ThenBy(e => e.Id).Take(8).ToList();
            var result = new DashboardResponse(DateTimeOffset.UtcNow, equipment.Count, equipment.Count - loaned, loaned,
                equipment.Count(e => HasFaults(e.Id)), hardware.Values.Sum(), manual.Values.Sum(), pending, categories, attention);
            await transaction.CommitAsync(ct);
            return Results.Ok(result);
        }).RequireAuthorization("EquipmentManager").WithTags("Dashboard");
        return endpoints;
    }
}
