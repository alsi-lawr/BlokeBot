using BlokeBot.Core.Features.Points.Balances;

namespace BlokeBot.Core.Features.Points.WatchTime;

internal sealed partial class WatchTimeRuntime
{
    internal async Task ProcessDueAsync(CancellationToken ct)
    {
        WatchTimeEpoch[] due;
        lock (_gate)
        {
            due = _epochs.Values.Where(value => Due(value) <= clock.GetUtcNow()).ToArray();
        }
        foreach (var captured in due)
        {
            var now = clock.GetUtcNow();
            var ordinal = Math.Max(
                captured.NextOrdinal,
                (now - captured.Anchor).Ticks / Interval.Ticks
            );
            var start = captured.Anchor + TimeSpan.FromTicks(Interval.Ticks * ordinal);
            lock (_gate)
            {
                if (
                    !_epochs.TryGetValue(captured.Settings.HostId, out var current)
                    || current != captured
                )
                {
                    continue;
                }
                _epochs[captured.Settings.HostId] = captured with { NextOrdinal = ordinal + 1 };
                _ = _observations.Remove(captured.Settings.HostId);
            }
            var read = await eligibility.ReadAsync(captured.Settings, ct);
            var credited = false;
            var kind = await read.Match<Task<WatchTimeStatusKind>>(
                async complete =>
                {
                    var observation = new WatchTimeObservation(
                        new(captured.Id, ordinal),
                        captured,
                        complete.BotUserId,
                        complete.BotLogin,
                        complete.StreamId,
                        complete.StreamStartedAt,
                        start,
                        start + Interval,
                        PointAmount.ParseAbsolute(captured.Settings.Amount),
                        complete.Targets
                    );
                    lock (_gate)
                    {
                        if (
                            !_epochs.TryGetValue(captured.Settings.HostId, out var current)
                            || current.Id != captured.Id
                            || current.Settings != captured.Settings
                            || !Connected(captured.Settings.Login)
                        )
                        {
                            return WatchTimeStatusKind.Waiting;
                        }
                        _observations[captured.Settings.HostId] = observation;
                    }
                    if (!IsCurrent(observation))
                    {
                        return WatchTimeStatusKind.Waiting;
                    }
                    foreach (var target in observation.Targets)
                    {
                        var outcome = await balances.CreditWatchTimeAsync(
                            observation,
                            target,
                            this,
                            clock,
                            ct
                        );
                        var action = outcome.Match(
                            value =>
                            {
                                credited = true;
                                return CreditProgress.Continue;
                            },
                            _ => CreditProgress.Continue,
                            _ => CreditProgress.Stop,
                            _ => CreditProgress.Continue,
                            _ => CreditProgress.Unavailable
                        );
                        if (action == CreditProgress.Unavailable)
                        {
                            return WatchTimeStatusKind.Unavailable;
                        }
                        if (action == CreditProgress.Stop)
                        {
                            break;
                        }
                    }
                    return WatchTimeStatusKind.Active;
                },
                _ => Task.FromResult(WatchTimeStatusKind.Offline),
                _ => Task.FromResult(WatchTimeStatusKind.Unavailable)
            );
            lock (_gate)
            {
                if (
                    _epochs.TryGetValue(captured.Settings.HostId, out var current)
                    && current.Id == captured.Id
                )
                {
                    _statuses[captured.Settings.HostId] = new(
                        kind,
                        current.Settings.Amount,
                        Due(current)
                    );
                }
            }
            if (credited)
            {
                _ = await pointsChanges.NotifyChangedAsync(ct);
            }
            await NotifyStatusChangedAsync();
        }
    }

    private enum CreditProgress
    {
        Continue,
        Stop,
        Unavailable,
    }
}
