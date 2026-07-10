namespace BlokeBot.Persistence.Models;

public enum GuessRoundStatus
{
    [PersistedToken("Open")]
    Open,

    [PersistedToken("Closed")]
    Closed,

    [PersistedToken("Completed")]
    Completed,
}
