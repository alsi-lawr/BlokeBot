namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Identifies one in-process hosted-channel runtime session.
/// </summary>
public sealed class BotChannelSessionIdentity
{
    private BotChannelSessionIdentity() { }

    /// <summary>
    /// Creates an opaque identity for a new hosted-channel runtime session.
    /// </summary>
    public static BotChannelSessionIdentity Create() => new();
}

/// <summary>
/// Names a channel and the in-process runtime session that should serve it.
/// </summary>
/// <param name="Channel">The channel login, without a leading hash character.</param>
/// <param name="SessionIdentity">The opaque identity of the active runtime session.</param>
public sealed record BotChannelTarget(string Channel, BotChannelSessionIdentity SessionIdentity);
