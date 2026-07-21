namespace BlokeBot.Persistence.Models;

public sealed class HostBotAccountSettings
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public bool OverrideEnabled { get; set; }

    public bool WhisperResponsesEnabled { get; set; }

    public string? TwitchUserId { get; set; }

    public string? Login { get; set; }

    public string? DisplayName { get; set; }

    public string? ProfileImageUrl { get; set; }

    public byte[]? ProtectedTokenPayload { get; set; }

    public DateTime? AuthorizedAtUtc { get; set; }

    public string? AuthorizedScopes { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
