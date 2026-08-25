using System.Diagnostics;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels;

public abstract record HostFeatureUpdateResult
{
    private HostFeatureUpdateResult() { }

    public sealed record Saved(HostFeatureActivationResult Activation) : HostFeatureUpdateResult;

    public sealed record Unchanged : HostFeatureUpdateResult;

    public sealed record HostNotFound : HostFeatureUpdateResult;
}

public sealed class HostFeatureService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostFeatureActivationAuthority activation,
    TimeProvider timeProvider
)
{
    public HostFeatureService(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        HostFeatureActivationAuthority activation
    )
        : this(dbFactory, activation, TimeProvider.System) { }

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

    public Task<HostFeatureUpdateResult> EnableAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken ct
    ) => UpdateAsync(hostId, feature, static (current, selected) => current | selected, ct);

    public Task<HostFeatureUpdateResult> DisableAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken ct
    ) => UpdateAsync(hostId, feature, static (current, selected) => current & ~selected, ct);

    private async Task<HostFeatureUpdateResult> UpdateAsync(
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
            return new HostFeatureUpdateResult.HostNotFound();
        }

        var updated = update(host.EnabledFeatures, feature);
        if (updated == host.EnabledFeatures)
        {
            return new HostFeatureUpdateResult.Unchanged();
        }

        var previous = host.EnabledFeatures;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await HostFeatureTransitionStager.StageAsync(db, host, updated, now, ct);
        _ = await db.SaveChangesAsync(ct);
        var enabled = updated & ~previous;
        var disabled = previous & ~updated;
        return new HostFeatureUpdateResult.Saved(
            await activation.ApplyAsync(hostId, enabled, disabled, ct)
        );
    }
}
