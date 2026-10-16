namespace BlokeBot.Persistence.Models;

public sealed class AutomationScheduleSkip
{
    public Guid Id { get; set; }
    public int HostId { get; set; }
    public Guid FlowId { get; set; }
    public Guid SourceNodeId { get; set; }
    public int Reason { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
