namespace BlokeBot.Persistence.Models;

public enum CustomCommandInvocationResetScope
{
    [PersistedToken("OneViewer")]
    OneViewer,

    [PersistedToken("AllViewers")]
    AllViewers,
}
