namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Collects the single account-provider policy for a Twitch bot registration.
/// </summary>
public sealed class TwitchBotAccountProviderSelection
{
    private readonly List<TwitchBotAccountProviderRegistration> _selections = [];

    /// <summary>
    /// Selects the runtime's built-in configured account provider.
    /// </summary>
    public TwitchBotAccountProviderSelection UseDefaultProvider()
    {
        _selections.Add(
            new TwitchBotAccountProviderRegistration
            {
                Kind = TwitchBotAccountProviderKind.Default,
                ProviderType = typeof(DefaultTwitchBotAccountProvider),
            }
        );
        return this;
    }

    /// <summary>
    /// Selects a feature-owned singleton that resolves an account for each hosted channel.
    /// </summary>
    /// <typeparam name="TProvider">The feature-owned hosted-channel provider.</typeparam>
    public TwitchBotAccountProviderSelection UseHostedChannelProvider<TProvider>()
        where TProvider : class, ITwitchBotAccountProvider
    {
        _selections.Add(
            new TwitchBotAccountProviderRegistration
            {
                Kind = TwitchBotAccountProviderKind.HostedChannel,
                ProviderType = typeof(TProvider),
            }
        );
        return this;
    }

    /// <summary>
    /// Selects a custom account-provider implementation owned by the caller.
    /// </summary>
    /// <typeparam name="TProvider">The custom account-provider implementation.</typeparam>
    public TwitchBotAccountProviderSelection UseCustomProvider<TProvider>()
        where TProvider : class, ITwitchBotAccountProvider
    {
        _selections.Add(
            new TwitchBotAccountProviderRegistration
            {
                Kind = TwitchBotAccountProviderKind.Custom,
                ProviderType = typeof(TProvider),
            }
        );
        return this;
    }

    internal TwitchBotAccountProviderRegistration RequireSingle()
    {
        return _selections.Count switch
        {
            1 => _selections[0],
            0 => throw new InvalidOperationException(
                "Exactly one Twitch bot account provider must be selected; none was selected."
            ),
            _ => throw new InvalidOperationException(
                $"Exactly one Twitch bot account provider must be selected; {_selections.Count} were selected."
            ),
        };
    }
}

internal enum TwitchBotAccountProviderKind
{
    Default,
    HostedChannel,
    Custom,
}

internal sealed record TwitchBotAccountProviderRegistration
{
    public required TwitchBotAccountProviderKind Kind { get; init; }

    public required Type ProviderType { get; init; }
}
