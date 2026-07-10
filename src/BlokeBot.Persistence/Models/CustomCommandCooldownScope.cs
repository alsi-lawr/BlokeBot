namespace BlokeBot.Persistence.Models;

public enum CustomCommandCooldownScope
{
    [PersistedToken("Global")]
    Global,

    [PersistedToken("User")]
    User,
}
