namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Collects the single channel-lifecycle notifier policy for a Twitch bot registration.
/// </summary>
public sealed class TwitchBotChannelLifecycleNotifierSelection
{
    private readonly List<TwitchBotChannelLifecycleNotifierRegistration> _selections = [];

    /// <summary>
    /// Selects the runtime's no-op channel-lifecycle notifier.
    /// </summary>
    public TwitchBotChannelLifecycleNotifierSelection UseNoOpNotifier()
    {
        _selections.Add(
            new TwitchBotChannelLifecycleNotifierRegistration
            {
                Kind = TwitchBotChannelLifecycleNotifierKind.NoOp,
                NotifierType = typeof(NoOpTwitchBotChannelLifecycleNotifier),
            }
        );
        return this;
    }

    /// <summary>
    /// Selects a feature-owned hosted-channel lifecycle notifier.
    /// </summary>
    /// <typeparam name="TNotifier">The feature-owned lifecycle notifier.</typeparam>
    public TwitchBotChannelLifecycleNotifierSelection UseHostedNotifier<TNotifier>()
        where TNotifier : class, ITwitchBotChannelLifecycleNotifier
    {
        _selections.Add(
            new TwitchBotChannelLifecycleNotifierRegistration
            {
                Kind = TwitchBotChannelLifecycleNotifierKind.Hosted,
                NotifierType = typeof(TNotifier),
            }
        );
        return this;
    }

    internal TwitchBotChannelLifecycleNotifierRegistration RequireSingle()
    {
        return _selections.Count switch
        {
            1 => _selections[0],
            0 => throw new InvalidOperationException(
                "Exactly one Twitch bot channel-lifecycle notifier must be selected; none was selected."
            ),
            _ => throw new InvalidOperationException(
                $"Exactly one Twitch bot channel-lifecycle notifier must be selected; {_selections.Count} were selected."
            ),
        };
    }
}

internal enum TwitchBotChannelLifecycleNotifierKind
{
    NoOp,
    Hosted,
}

internal sealed record TwitchBotChannelLifecycleNotifierRegistration
{
    public required TwitchBotChannelLifecycleNotifierKind Kind { get; init; }

    public required Type NotifierType { get; init; }
}
