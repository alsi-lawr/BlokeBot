using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.HostedChannels.Runtime;

public abstract record HostedChannelRuntimeLifecycle
{
    private HostedChannelRuntimeLifecycle() { }

    public abstract TResult Match<TResult>(
        Func<Stopped, TResult> stopped,
        Func<Starting, TResult> starting,
        Func<Started, TResult> started,
        Func<Stopping, TResult> stopping
    );

    internal static HostedChannelRuntimeLifecycle FromPersistence(
        BotChannelRuntimeState state,
        DateTime? changedAtUtc
    )
    {
        return state switch
        {
            BotChannelRuntimeState.Stopped => new Stopped(changedAtUtc),
            BotChannelRuntimeState.Starting when changedAtUtc is { } starting => new Starting(
                starting
            ),
            BotChannelRuntimeState.Started when changedAtUtc is { } started => new Started(started),
            BotChannelRuntimeState.Stopping when changedAtUtc is { } stopping => new Stopping(
                stopping
            ),
            _ => throw new PersistenceDataIntegrityException(typeof(BotHost)),
        };
    }

    public sealed record Stopped : HostedChannelRuntimeLifecycle
    {
        internal Stopped(DateTime? changedAtUtc)
        {
            ChangedAtUtc = changedAtUtc;
        }

        public DateTime? ChangedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Stopped, TResult> stopped,
            Func<Starting, TResult> starting,
            Func<Started, TResult> started,
            Func<Stopping, TResult> stopping
        )
        {
            return stopped(this);
        }
    }

    public sealed record Starting : HostedChannelRuntimeLifecycle
    {
        internal Starting(DateTime changedAtUtc)
        {
            ChangedAtUtc = changedAtUtc;
        }

        public DateTime ChangedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Stopped, TResult> stopped,
            Func<Starting, TResult> starting,
            Func<Started, TResult> started,
            Func<Stopping, TResult> stopping
        )
        {
            return starting(this);
        }
    }

    public sealed record Started : HostedChannelRuntimeLifecycle
    {
        internal Started(DateTime changedAtUtc)
        {
            ChangedAtUtc = changedAtUtc;
        }

        public DateTime ChangedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Stopped, TResult> stopped,
            Func<Starting, TResult> starting,
            Func<Started, TResult> started,
            Func<Stopping, TResult> stopping
        )
        {
            return started(this);
        }
    }

    public sealed record Stopping : HostedChannelRuntimeLifecycle
    {
        internal Stopping(DateTime changedAtUtc)
        {
            ChangedAtUtc = changedAtUtc;
        }

        public DateTime ChangedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Stopped, TResult> stopped,
            Func<Starting, TResult> starting,
            Func<Started, TResult> started,
            Func<Stopping, TResult> stopping
        )
        {
            return stopping(this);
        }
    }
}
