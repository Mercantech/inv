namespace Inv.Web.Models;
public sealed record DashboardCategory(Guid Id, string Name, int Total, int Loaned, int Available, int WithFaults);
public sealed record DashboardEquipment(Guid Id, int ShortId, string Name, string CategoryName, bool IsLoaned, int HardwareFaults, int ManualFaults);
public sealed record DashboardResponse(DateTimeOffset GeneratedAt, int TotalEquipment, int AvailableEquipment,
    int LoanedEquipment, int FaultyEquipment, int HardwareFaultCount, int ManualFaultCount, int PendingRequests,
    IReadOnlyList<DashboardCategory> Categories, IReadOnlyList<DashboardEquipment> Attention);
