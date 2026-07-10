namespace BlokeBot.Persistence.Models;

public enum CustomMessageSelectionMode
{
    [PersistedToken("First")]
    First,

    [PersistedToken("Sequential")]
    Sequential,

    [PersistedToken("Random")]
    Random,
}
