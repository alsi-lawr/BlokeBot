namespace BlokeBot.Persistence.Models;

public enum ConfigurationActivationStatus
{
    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Processing")]
    Processing,

    [PersistedToken("Complete")]
    Complete,

    [PersistedToken("Failed")]
    Failed,
}

public sealed class ConfigurationActivation
{
    public Guid Id { get; set; }

    public int HostId { get; set; }

    public HostFeatureFlags EnabledChanges { get; set; }

    public HostFeatureFlags DisabledChanges { get; set; }

    public ConfigurationActivationStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public long Revision { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? FailureCode { get; set; }
}
