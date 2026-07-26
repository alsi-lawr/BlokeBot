namespace BlokeBot.Persistence.Models;

public enum TwitchStreamMarkerStatus
{
    [PersistedToken("Succeeded")]
    Succeeded,

    [PersistedToken("Failed")]
    Failed,

    [PersistedToken("Ambiguous")]
    Ambiguous,
}
