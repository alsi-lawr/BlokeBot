using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.WatchTime;

internal sealed partial class WatchTimeRuntime(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelRuntimeTransitionService sessions,
    IBotRuntimeStatusAccessor botRuntime,
    BotSettings botSettings,
    WatchTimeEligibilityReader eligibility,
    PointBalanceService balances,
    PointsChangeNotifier pointsChanges,
    TimeProvider clock,
    ILogger<WatchTimeRuntime> logger
) : BackgroundService, IWatchTimeSettingsCommitObserver
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Dictionary<
        string,
        (BotChannelTarget Target, DateTimeOffset Started)
    > _accepted = new(StringComparer.Ordinal);
    private readonly Dictionary<
        int,
        (WatchTimeSettingsWriteResult Result, DateTimeOffset Observed)
    > _settingsReceipts = [];
    private readonly Dictionary<int, WatchTimeEpoch> _epochs = [];
    private readonly Dictionary<int, WatchTimeObservation> _observations = [];
    private readonly Dictionary<int, WatchTimeStatus> _statuses = [];
    private bool _subscribed;

    internal event Func<Task>? StatusChanged;
    internal string DefaultBotLogin => LoginName.Parse(botSettings.Identity.BotUsername).Value;

    internal WatchTimeStatus? GetStatus(int hostId)
    {
        lock (_gate)
        {
            return _statuses.GetValueOrDefault(hostId);
        }
    }

    public void SettingsCommitted(
        int hostId,
        WatchTimeSettingsWriteResult result,
        DateTimeOffset receiptUtc
    )
    {
        lock (_gate)
        {
            if (
                !_settingsReceipts.TryGetValue(hostId, out var previous)
                || previous.Observed <= receiptUtc
            )
            {
                _settingsReceipts[hostId] = (result, receiptUtc);
            }
        }
        Wake();
    }

    internal void AcceptedStarted(BotChannelTarget target)
    {
        var login = LoginName.Parse(target.Channel).Value;
        lock (_gate)
        {
            if (!Connected(login))
            {
                return;
            }
            if (
                !_accepted.TryGetValue(login, out var current)
                || !ReferenceEquals(current.Target.SessionIdentity, target.SessionIdentity)
            )
            {
                _accepted[login] = (target with { Channel = login }, clock.GetUtcNow());
                DropChannel(login);
            }
        }
        Wake();
    }

    internal void AcceptedStopped(BotChannelTarget target)
    {
        var login = LoginName.Parse(target.Channel).Value;
        lock (_gate)
        {
            if (
                _accepted.TryGetValue(login, out var current)
                && ReferenceEquals(current.Target.SessionIdentity, target.SessionIdentity)
            )
            {
                _ = _accepted.Remove(login);
                DropChannel(login);
            }
        }
        Wake();
    }

    private bool Connected(string login) =>
        botRuntime.Current.Match(
            _ => false,
            _ => false,
            value => value.Channels.Any(channel => LoginName.Parse(channel).Value == login)
        );

    private void RuntimeChanged()
    {
        lock (_gate)
        {
            foreach (var login in _accepted.Keys.Where(login => !Connected(login)).ToArray())
            {
                _ = _accepted.Remove(login);
                DropChannel(login);
            }
        }
        Wake();
    }

    private void DropChannel(string login)
    {
        foreach (
            var epoch in _epochs.Values.Where(value => value.Settings.Login == login).ToArray()
        )
        {
            _ = _epochs.Remove(epoch.Settings.HostId);
            _ = _observations.Remove(epoch.Settings.HostId);
            _statuses[epoch.Settings.HostId] = new(
                WatchTimeStatusKind.Waiting,
                epoch.Settings.Amount,
                null
            );
        }
    }

    internal bool IsCurrent(WatchTimeObservation observation)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            return now >= observation.OpportunityStart
                && now < observation.OpportunityEnd
                && _epochs.TryGetValue(observation.Epoch.Settings.HostId, out var epoch)
                && epoch.Id == observation.Tick.Epoch
                && epoch.Settings.Revision == observation.Epoch.Settings.Revision
                && epoch.Settings.Generation == observation.Epoch.Settings.Generation
                && _observations.TryGetValue(epoch.Settings.HostId, out var receipt)
                && ReferenceEquals(receipt, observation)
                && _accepted.TryGetValue(epoch.Settings.Login, out var accepted)
                && ReferenceEquals(
                    accepted.Target.SessionIdentity,
                    observation.Epoch.Target.SessionIdentity
                )
                && Connected(epoch.Settings.Login);
        }
    }

    private async Task NotifyStatusChangedAsync()
    {
        if (StatusChanged is not { } changed)
        {
            return;
        }
        foreach (var subscriber in changed.GetInvocationList().Cast<Func<Task>>())
        {
            await subscriber();
        }
    }

    private static DateTimeOffset Due(WatchTimeEpoch epoch) =>
        epoch.Anchor + TimeSpan.FromTicks(Interval.Ticks * epoch.NextOrdinal);

    private void Wake()
    {
        lock (_gate)
        {
            if (_wake.CurrentCount == 0)
            {
                _ = _wake.Release();
            }
        }
    }

    public override void Dispose()
    {
        if (_subscribed)
        {
            botRuntime.Changed -= RuntimeChanged;
        }
        base.Dispose();
        _wake.Dispose();
    }
}
