using System.Net;

namespace BlokeBot.Twitch;

public enum WhisperSendStatus
{
    Accepted,
    RateLimited,
    Rejected,
}

public sealed record WhisperSendResult
{
    public required WhisperSendStatus Status { get; init; }

    public required HttpStatusCode StatusCode { get; init; }

    public string? ResponseBody { get; init; }

    public override string ToString()
    {
        return $"{nameof(WhisperSendResult)} {{ Status = {Status}, StatusCode = {StatusCode}, ResponseBody = [redacted] }}";
    }
}
