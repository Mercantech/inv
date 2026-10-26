namespace Inv.Web.Models;

public sealed record ComponentResult(string Name, bool Ok, bool Skipped, string? Note);
public sealed record TestResult(Guid Id, DateTimeOffset TestedAt, bool Passed, int PassedCount,
    int FailedCount, List<ComponentResult> Components, bool Imported, bool HasComponentDetails);
public sealed record DeviceResult(string SamdId, string? Label, string? Mac, string? EccSerial,
    Guid? EquipmentId, string? EquipmentName, DateTimeOffset LastSeenAt, int TestCount, TestResult? LatestTest, int ShortId = 0, int? EquipmentShortId = null);
public sealed record ImportResult(int DevicesAdded, int ReportsAdded, int ReportsSkipped);
