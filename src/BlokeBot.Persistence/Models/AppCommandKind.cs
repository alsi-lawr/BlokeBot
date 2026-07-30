namespace BlokeBot.Persistence.Models;

public enum AppCommandKind
{
    [PersistedToken("Commands")]
    Commands,

    [PersistedToken("Start")]
    Start,

    [PersistedToken("Stop")]
    Stop,

    [PersistedToken("Win")]
    Win,

    [PersistedToken("Guess")]
    Guess,

    [PersistedToken("Guesses")]
    Guesses,

    [PersistedToken("Points")]
    Points,

    [PersistedToken("GivePoints")]
    GivePoints,

    [PersistedToken("AddPoints")]
    AddPoints,

    [PersistedToken("RemovePoints")]
    RemovePoints,

    [PersistedToken("Gamble")]
    Gamble,

    [PersistedToken("Giveaway")]
    Giveaway,

    [PersistedToken("Join")]
    Join,

    [PersistedToken("EndGiveaway")]
    EndGiveaway,

    [PersistedToken("CancelGiveaway")]
    CancelGiveaway,
}
