using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.WatchTime;

internal sealed partial class WatchTimeRuntime
{
    internal async Task ReconcileAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await WatchTimeHostQueries.Snapshot(db, enabledOnly: true).ToArrayAsync(ct);
        var seen = new HashSet<int>();
        foreach (var host in hosts)
        {
            lock (_gate)
            {
                if (!_epochs.ContainsKey(host.HostId))
                {
                    _statuses[host.HostId] = new(WatchTimeStatusKind.Waiting, host.Amount, null);
                }
            }
            if (
                host.RuntimeState != BotChannelRuntimeState.Started
                || host.Generation == Guid.Empty
                || host.Amount is null
            )
            {
                continue;
            }
            var target = await sessions.GetCurrentSessionTargetAsync(host.HostId, host.Login, ct);
            if (target is null)
            {
                continue;
            }
            lock (_gate)
            {
                if (!Connected(host.Login))
                {
                    continue;
                }
                _ = seen.Add(host.HostId);
                var now = clock.GetUtcNow();
                if (
                    !_accepted.TryGetValue(host.Login, out var accepted)
                    || !ReferenceEquals(accepted.Target.SessionIdentity, target.SessionIdentity)
                )
                {
                    accepted = (target, now);
                    _accepted[host.Login] = accepted;
                }
                if (
                    _epochs.TryGetValue(host.HostId, out var current)
                    && current.Settings.Generation == host.Generation
                    && ReferenceEquals(current.Target.SessionIdentity, target.SessionIdentity)
                    && current.Settings.Account(DefaultBotLogin) == host.Account(DefaultBotLogin)
                )
                {
                    if (current.Settings != host)
                    {
                        _epochs[host.HostId] = current with { Settings = host };
                        _ = _observations.Remove(host.HostId);
                    }
                }
                else
                {
                    var observedEnable =
                        _settingsReceipts.TryGetValue(host.HostId, out var receipt)
                        && receipt.Result.Enabled
                        && receipt.Result.EnableGeneration == host.Generation
                            ? receipt.Observed
                            : now;
                    var anchor =
                        accepted.Started > observedEnable ? accepted.Started : observedEnable;
                    _epochs[host.HostId] = new(Guid.NewGuid(), host, target, anchor, 1);
                    _ = _observations.Remove(host.HostId);
                    _statuses[host.HostId] = new(
                        WatchTimeStatusKind.Waiting,
                        host.Amount,
                        anchor + Interval
                    );
                }
                var epoch = _epochs[host.HostId];
                var status =
                    _statuses.GetValueOrDefault(host.HostId)
                    ?? new(WatchTimeStatusKind.Waiting, host.Amount, null);
                _statuses[host.HostId] = status with { Amount = host.Amount, NextDue = Due(epoch) };
            }
        }
        lock (_gate)
        {
            foreach (var id in _epochs.Keys.Where(id => !seen.Contains(id)).ToArray())
            {
                _ = _epochs.Remove(id);
                _ = _observations.Remove(id);
                _ = _settingsReceipts.Remove(id);
                _statuses[id] = hosts.FirstOrDefault(value => value.HostId == id) is { } enabled
                    ? new(WatchTimeStatusKind.Waiting, enabled.Amount, null)
                    : new(WatchTimeStatusKind.Off, null, null);
            }
        }
        lock (_gate)
        {
            foreach (
                var id in _statuses
                    .Keys.Where(id => !hosts.Any(host => host.HostId == id))
                    .ToArray()
            )
            {
                _ = _settingsReceipts.Remove(id);
                _statuses[id] = new(WatchTimeStatusKind.Off, null, null);
            }
        }
        await NotifyStatusChangedAsync();
    }
}
