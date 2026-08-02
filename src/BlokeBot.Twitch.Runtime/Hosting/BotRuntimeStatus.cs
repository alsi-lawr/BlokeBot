using System.Collections.Immutable;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Describes the latest observed Twitch bot runtime lifecycle state.
/// </summary>
public abstract record BotRuntimeStatus
{
    private BotRuntimeStatus() { }

    /// <summary>
    /// Dispatches to the handler for the current lifecycle state.
    /// </summary>
    public abstract TResult Match<TResult>(
        Func<Unauthorized, TResult> unauthorized,
        Func<Authorized, TResult> authorized,
        Func<Connected, TResult> connected
    );

    /// <summary>
    /// The runtime does not currently have a usable bot access token.
    /// </summary>
    public sealed record Unauthorized : BotRuntimeStatus
    {
        public override TResult Match<TResult>(
            Func<Unauthorized, TResult> unauthorized,
            Func<Authorized, TResult> authorized,
            Func<Connected, TResult> connected
        ) => unauthorized(this);
    }

    /// <summary>
    /// The runtime has a usable bot access token but no active chat connection.
    /// </summary>
    public sealed record Authorized : BotRuntimeStatus
    {
        public override TResult Match<TResult>(
            Func<Unauthorized, TResult> unauthorized,
            Func<Authorized, TResult> authorized,
            Func<Connected, TResult> connected
        ) => authorized(this);
    }

    /// <summary>
    /// The runtime is connected to one or more channels with a usable bot access token.
    /// </summary>
    public sealed record Connected : BotRuntimeStatus
    {
        public Connected(IEnumerable<string> channels)
        {
            ArgumentNullException.ThrowIfNull(channels);
            Channels = channels.ToImmutableArray();
            if (Channels.IsEmpty)
            {
                throw new ArgumentException(
                    "A connected runtime must include at least one channel.",
                    nameof(channels)
                );
            }
        }

        /// <summary>
        /// Gets the channels currently connected by the runtime.
        /// </summary>
        public ImmutableArray<string> Channels { get; }

        public override TResult Match<TResult>(
            Func<Unauthorized, TResult> unauthorized,
            Func<Authorized, TResult> authorized,
            Func<Connected, TResult> connected
        ) => connected(this);
    }
}
