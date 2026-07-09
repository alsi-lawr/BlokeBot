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
    HttpStatusCode StatusCode,
    string? ResponseBody = null
)
{
    public bool IsAccepted => Status == TwitchWhisperSendStatus.Accepted;
}
