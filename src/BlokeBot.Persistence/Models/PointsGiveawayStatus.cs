namespace BlokeBot.Persistence.Models;

public enum PointsGiveawayStatus
{
    [PersistedToken("Active")]
    Active,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Cancelled")]
    Cancelled,

    [PersistedToken("Expired")]
    Expired,
}
