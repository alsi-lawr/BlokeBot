namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Collects the single command-response sender policy for a Twitch bot registration.
/// </summary>
public sealed class TwitchCommandResponseSenderSelection
{
    private readonly List<TwitchCommandResponseSenderRegistration> _selections = [];

    /// <summary>
    /// Selects standalone delivery of every command response through public chat.
    /// </summary>
    public TwitchCommandResponseSenderSelection UseStandalonePublicChat()
    {
        _selections.Add(
            new TwitchCommandResponseSenderRegistration
            {
                Kind = TwitchCommandResponseSenderKind.StandalonePublicChat,
                SenderType = typeof(TwitchChatCommandResponseSender),
            }
        );
        return this;
    }

    /// <summary>
    /// Selects a feature-owned hosted sender that can deliver private whisper responses.
    /// </summary>
    /// <typeparam name="TSender">The feature-owned hosted response sender.</typeparam>
    public TwitchCommandResponseSenderSelection UseHostedWhisperSender<TSender>()
        where TSender : class, ITwitchCommandResponseSender
    {
        _selections.Add(
            new TwitchCommandResponseSenderRegistration
            {
                Kind = TwitchCommandResponseSenderKind.HostedWhisper,
                SenderType = typeof(TSender),
            }
        );
        return this;
    }

    internal TwitchCommandResponseSenderRegistration RequireSingle()
    {
        return _selections.Count switch
        {
            1 => _selections[0],
            0 => throw new InvalidOperationException(
                "Exactly one Twitch command-response sender must be selected; none was selected."
            ),
            _ => throw new InvalidOperationException(
                $"Exactly one Twitch command-response sender must be selected; {_selections.Count} were selected."
            ),
        };
    }
}

internal enum TwitchCommandResponseSenderKind
{
    StandalonePublicChat,
    HostedWhisper,
}

internal sealed record TwitchCommandResponseSenderRegistration
{
    public required TwitchCommandResponseSenderKind Kind { get; init; }

    public required Type SenderType { get; init; }
}
