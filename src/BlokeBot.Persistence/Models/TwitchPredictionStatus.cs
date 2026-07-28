namespace BlokeBot.Persistence.Models;

public enum TwitchPredictionStatus
{
    [PersistedToken("Active")]
    Active,

    [PersistedToken("Locked")]
    Locked,

    [PersistedToken("Resolved")]
    Resolved,

    [PersistedToken("Canceled")]
    Canceled,

    [PersistedToken("Archived")]
    Archived,
}
