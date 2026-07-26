namespace BlokeBot.Persistence.Models;

public sealed class HostBroadcasterAuthorization
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public byte[]? ProtectedTokenPayload { get; set; }

    public string? TwitchUserId { get; set; }

    public string? Login { get; set; }

    public string? AuthorizedScopes { get; set; }

    public DateTime? AuthorizedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
