namespace BlokeBot.Persistence.Models;

public enum TwitchRewardRedemptionStatus
{
    [PersistedToken("Unfulfilled")]
    Unfulfilled,

    [PersistedToken("Fulfilled")]
    Fulfilled,

    [PersistedToken("Canceled")]
    Canceled,
}
