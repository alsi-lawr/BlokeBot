namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed record HostedChannelRuntimeControlResult(
    bool Succeeded,
    string Message,
    DateTime? NextAllowedAtUtc = null
)
{
    public static HostedChannelRuntimeControlResult Success(string message)
    {
        return new(true, message);
    }

    public static HostedChannelRuntimeControlResult Failure(
        string message,
        DateTime? nextAllowedAtUtc = null
    )
    {
        return new(false, message, nextAllowedAtUtc);
    }
}
