using System.Net;

namespace BlokeBot.Twitch.Runtime;

public enum TwitchWhisperSendStatus
{
    Accepted,
    RateLimited,
    Rejected,
}

public sealed record TwitchWhisperSendResult(
    TwitchWhisperSendStatus Status,
    HttpStatusCode StatusCode
)
{
    public bool IsAccepted => Status == TwitchWhisperSendStatus.Accepted;
}
