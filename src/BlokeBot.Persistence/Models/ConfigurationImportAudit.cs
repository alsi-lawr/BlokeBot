namespace BlokeBot.Persistence.Models;

public sealed class ConfigurationImportAudit
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public Guid OperationId { get; set; }

    public string ActorTwitchUserId { get; set; } = string.Empty;

    public string ActorLogin { get; set; } = string.Empty;

    public int SourceFormatVersion { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string SummaryJson { get; set; } = "{}";
}
