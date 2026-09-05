using System.Collections.Immutable;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

internal sealed record PortalProjectors(
    PortalActivityProjectors Activities,
    PortalDirectoryProjectors Directories,
    PortalPersonalProjectors Personal
);

internal sealed class ViewerPortalCatalogueService(
    ViewerPortalAccess access,
    PortalProjectors projectors,
    PortalReadScheduler scheduler,
    PortalProjectionRunner runner
)
{
    public async Task<PortalCatalogueSnapshot> ReadAsync(
        PortalChannel channel,
        PortalIdentity identity,
        CancellationToken cancellationToken,
        IReadOnlySet<HostFeatureFlags>? featureKinds = null
    )
    {
        try
        {
            return await ReadCoreAsync(channel, identity, cancellationToken, featureKinds);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Snapshot(
                channel.Host,
                identity,
                ViewerPortalCatalogue
                    .Descriptors.Where(value =>
                        channel.PublicFeatures.Contains(value.Feature)
                        && (featureKinds is null || featureKinds.Contains(value.Feature))
                    )
                    .Select(value => new PortalFeatureProjection(
                        value,
                        new PortalSummaryOutcome.Unavailable()
                    ))
                    .ToImmutableArray()
            );
        }
    }

    private async Task<PortalCatalogueSnapshot> ReadCoreAsync(
        PortalChannel channel,
        PortalIdentity identity,
        CancellationToken cancellationToken,
        IReadOnlySet<HostFeatureFlags>? featureKinds
    )
    {
        var current = await CurrentChannelAsync(channel.Host, cancellationToken);
        if (current is null)
        {
            return Snapshot(channel.Host, identity, []);
        }
        var selected = ViewerPortalCatalogue
            .Descriptors.Where(descriptor =>
                current.PublicFeatures.Contains(descriptor.Feature)
                && (featureKinds is null || featureKinds.Contains(descriptor.Feature))
            )
            .ToArray();
        var results = await Task.WhenAll(
            selected.Select(async descriptor => new PortalFeatureProjection(
                descriptor,
                await runner.ReadAsync(
                    channel.Host.Id,
                    descriptor,
                    ct =>
                        scheduler.ReadAsync(
                            token => descriptor.ProjectAsync(projectors, current, identity, token),
                            ct
                        ),
                    cancellationToken
                )
            ))
        );
        cancellationToken.ThrowIfCancellationRequested();
        var after = await CurrentChannelAsync(channel.Host, cancellationToken);
        return Snapshot(
            channel.Host,
            identity,
            after is null
                ? []
                : results
                    .Where(result =>
                        after.PublicFeatures.Contains(result.Descriptor.Feature)
                        && result.Outcome.Match(
                            available: static _ => true,
                            empty: static _ => true,
                            disabled: static _ => false,
                            degraded: static _ => true,
                            unavailable: static _ => true,
                            unauthorized: static _ => false
                        )
                    )
                    .ToImmutableArray()
        );
    }

    private async Task<PortalChannel?> CurrentChannelAsync(
        PortalHostKey expected,
        CancellationToken ct
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var result = await scheduler
            .ReadAsync(token => access.ResolveChannelAsync(expected.Login, token), timeout.Token)
            .WaitAsync(timeout.Token);
        return result.Match<PortalChannel?>(
            resolved => resolved.Channel.Host.Id == expected.Id ? resolved.Channel : null,
            static _ => null
        );
    }

    private static PortalCatalogueSnapshot Snapshot(
        PortalHostKey host,
        PortalIdentity identity,
        ImmutableArray<PortalFeatureProjection> features
    ) =>
        new(
            host,
            PortalCacheScope.For(host, identity),
            features,
            PortalSummaryBounds.Merge(
                features
                    .Where(feature => feature.Descriptor.Audience == PortalAudience.Public)
                    .SelectMany(feature =>
                        feature.Outcome.Match<IEnumerable<PortalActivity>>(
                            available: static value => value.Summary.RecentActivity,
                            empty: static _ => [],
                            disabled: static _ => [],
                            degraded: static value => value.Summary.RecentActivity,
                            unavailable: static _ => [],
                            unauthorized: static _ => []
                        )
                    )
            )
        );
}
