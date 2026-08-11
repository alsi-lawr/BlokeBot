using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels;

public interface IHostFeatureChangeObserver
{
    ValueTask FeatureChangedAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled,
        CancellationToken cancellationToken
    );
}

public sealed class HostFeatureService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes,
    IEnumerable<INativeTwitchFeatureChangeObserver> nativeTwitchObservers,
    IEnumerable<IHostFeatureChangeObserver> featureObservers,
    TimeProvider timeProvider
)
{
    public HostFeatureService(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        HostedChannelChangeNotifier changes,
        IEnumerable<INativeTwitchFeatureChangeObserver> nativeTwitchObservers
    )
        : this(dbFactory, changes, nativeTwitchObservers, [], TimeProvider.System) { }

    public HostFeatureService(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        HostedChannelChangeNotifier changes,
        IEnumerable<INativeTwitchFeatureChangeObserver> nativeTwitchObservers,
        IEnumerable<IHostFeatureChangeObserver> featureObservers
    )
        : this(dbFactory, changes, nativeTwitchObservers, featureObservers, TimeProvider.System) { }

    public async Task<IReadOnlyDictionary<int, HostFeatureFlags>> LoadHostedFeaturesAsync(
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await db
            .Hosts.AsNoTracking()
            .Select(x => new { x.Id, x.EnabledFeatures })
            .ToArrayAsync(ct);

        return hosts.ToDictionary(x => x.Id, x => x.EnabledFeatures);
    }

    public IO<Option<HostFeatureFlags>, Never> Load(int hostId) =>
        IO<Option<HostFeatureFlags>, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var features = await db
                .Hosts.AsNoTracking()
                .Where(x => x.Id == hostId)
                .Select(x => (HostFeatureFlags?)x.EnabledFeatures)
                .SingleOrDefaultAsync(ct);
            return Result<Option<HostFeatureFlags>, Never>.Success(
                features.HasValue
                    ? Option<HostFeatureFlags>.Some(features.Value)
                    : Option<HostFeatureFlags>.None
            );
        });

    public async Task<bool> IsEnabledAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken ct
    )
    {
        var result = await Load(hostId).ExecuteAsync(ct);
        return result.Match(
            features => features.Match(value => value.Contains(feature), () => false),
            _ => throw new UnreachableException()
        );
    }

    public Task EnableAsync(int hostId, HostFeatureFlags feature, CancellationToken ct) =>
        UpdateAsync(hostId, feature, static (current, selected) => current | selected, ct);

    public Task DisableAsync(int hostId, HostFeatureFlags feature, CancellationToken ct) =>
        UpdateAsync(hostId, feature, static (current, selected) => current & ~selected, ct);

    private async Task UpdateAsync(
        int hostId,
        HostFeatureFlags feature,
        Func<HostFeatureFlags, HostFeatureFlags, HostFeatureFlags> update,
        CancellationToken ct
    )
    {
        if (!HostFeatureCatalog.IsSelectable(feature))
        {
            throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return;
        }

        var updated = update(host.EnabledFeatures, feature);
        if (updated == host.EnabledFeatures)
        {
            return;
        }

        var bountyRequirements = HostFeatureFlags.Bounties | HostFeatureFlags.Points;
        if (
            host.EnabledFeatures.Contains(bountyRequirements)
            && !updated.Contains(bountyRequirements)
        )
        {
            host.BountiesPausedAtUtc ??= timeProvider.GetUtcNow().UtcDateTime;
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (
            host.EnabledFeatures.Contains(HostFeatureFlags.CommunityProgression)
            && !updated.Contains(HostFeatureFlags.CommunityProgression)
        )
        {
            host.CommunityProgressionPausedAtUtc ??= now;
        }
        if (
            !host.EnabledFeatures.Contains(HostFeatureFlags.CommunityProgression)
            && updated.Contains(HostFeatureFlags.CommunityProgression)
        )
        {
            host.CommunityProgressionAcceptEventsAfterUtc = now;
            host.CommunityProgressionPausedAtUtc = null;
        }
        if (
            host.EnabledFeatures.Contains(HostFeatureFlags.Bingo)
            && !updated.Contains(HostFeatureFlags.Bingo)
        )
        {
            host.BingoPausedAtUtc ??= now;
        }
        if (
            !host.EnabledFeatures.Contains(HostFeatureFlags.Bingo)
            && updated.Contains(HostFeatureFlags.Bingo)
        )
        {
            host.BingoAcceptEventsAfterUtc = now;
            host.BingoPausedAtUtc = null;
        }
        if (
            host.EnabledFeatures.Contains(HostFeatureFlags.Competitions)
            && !updated.Contains(HostFeatureFlags.Competitions)
        )
        {
            host.CompetitionsPausedAtUtc ??= now;
        }
        if (
            !host.EnabledFeatures.Contains(HostFeatureFlags.Competitions)
            && updated.Contains(HostFeatureFlags.Competitions)
        )
        {
            host.CompetitionsAcceptWorkAfterUtc = now;
            host.CompetitionsPausedAtUtc = null;
        }
        if (
            host.EnabledFeatures.Contains(HostFeatureFlags.RaidCollaboration)
            && !updated.Contains(HostFeatureFlags.RaidCollaboration)
        )
        {
            host.RaidCollaborationPausedAtUtc ??= now;
        }
        if (
            !host.EnabledFeatures.Contains(HostFeatureFlags.RaidCollaboration)
            && updated.Contains(HostFeatureFlags.RaidCollaboration)
        )
        {
            host.RaidCollaborationAcceptEventsAfterUtc = now;
            host.RaidCollaborationPausedAtUtc = null;
        }
        host.EnabledFeatures = updated;
        _ = await db.SaveChangesAsync(ct);
        foreach (var observer in featureObservers)
        {
            await observer.FeatureChangedAsync(hostId, feature, updated.Contains(feature), ct);
        }
        _ = await changes.NotifyChangedAsync(ct);
        if (!HostFeatureFlags.NativeTwitchFeatures.Contains(feature))
        {
            return;
        }

        var state = updated.Contains(feature)
            ? NativeTwitchFeatureState.Enabled
            : NativeTwitchFeatureState.Disabled;
        foreach (var observer in nativeTwitchObservers)
        {
            await observer.NativeTwitchFeatureChangedAsync(hostId, feature, state, ct);
        }
    }
}
