namespace BlokeBot.Persistence.Models;

public enum TwitchClipStatus
{
    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Available")]
    Available,

    [PersistedToken("Failed")]
    Failed,

    [PersistedToken("Expired")]
    Expired,

    [PersistedToken("Ambiguous")]
    Ambiguous,
}
