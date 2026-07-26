namespace BlokeBot.Persistence.Models;

public enum TwitchPollStatus
{
    [PersistedToken("Active")]
    Active,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Terminated")]
    Terminated,

    [PersistedToken("Archived")]
    Archived,
}
