namespace BlokeBot.Persistence.Models;

public enum PointsEligibilityMode
{
    [PersistedToken("everyone")]
    Everyone,

    [PersistedToken("subscribers")]
    Subscribers,

    [PersistedToken("followers")]
    Followers,
}
