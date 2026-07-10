namespace BlokeBot.Persistence.Models;

public enum DurableAlertSeverity
{
    [PersistedToken("Info")]
    Info,

    [PersistedToken("Warning")]
    Warning,

    [PersistedToken("Critical")]
    Critical,
}
