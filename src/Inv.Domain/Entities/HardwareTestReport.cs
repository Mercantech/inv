namespace Inv.Domain.Entities;

public sealed class HardwareTestReport : Entity
{
    public required string SamdId { get; set; }
    public HardwareDevice Device { get; set; } = null!;
    public bool Passed { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public DateTimeOffset TestedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ReportJson { get; set; }
    public string? Details { get; set; }
    public string? ImportSource { get; set; }
    public long? LegacyReportId { get; set; }
}
