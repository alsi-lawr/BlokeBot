namespace BlokeBot.Persistence.Models;

public sealed class AutomationTrace
{
    public Guid Id { get; set; }
    public int HostId { get; set; }
    public Guid? FlowId { get; set; }
    public Guid? ProductionRunId { get; set; }
    public int SchemaVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int EventCount { get; set; }
    public int ByteCount { get; set; }
    public int Truncation { get; set; }
}

public sealed class AutomationTraceEvent
{
    public Guid TraceId { get; set; }
    public int Sequence { get; set; }
    public string EventJson { get; set; } = string.Empty;
}
