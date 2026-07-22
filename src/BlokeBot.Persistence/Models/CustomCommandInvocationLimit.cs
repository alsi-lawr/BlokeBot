namespace BlokeBot.Persistence.Models;

public enum CustomCommandInvocationLimit
{
    [PersistedToken("Unlimited")]
    Unlimited,

    [PersistedToken("OncePerStream")]
    OncePerStream,

    [PersistedToken("OncePerUser")]
    OncePerUser,

    [PersistedToken("OncePerStreamPerUser")]
    OncePerStreamPerUser,
}
