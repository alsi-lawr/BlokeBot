namespace BlokeBot.Persistence.Models;

public sealed class DurableAlert
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public DurableAlertSeverity Severity { get; set; } = DurableAlertSeverity.Info;

    public string Source { get; set; } = string.Empty;

    public string SourceKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? LinkPath { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? AcknowledgedAtUtc { get; set; }

    public string? AcknowledgedByLogin { get; set; }
}
