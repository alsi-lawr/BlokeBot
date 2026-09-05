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
    PortalProjectors projectors
)
{
    public async Task<PortalCatalogueSnapshot> ReadAsync(
        PortalChannel channel,
        PortalIdentity identity,
        CancellationToken cancellationToken,
        IReadOnlySet<HostFeatureFlags>? featureKinds = null
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
                await PortalProjectionRunner.ReadAsync(
                    ct => descriptor.ProjectAsync(projectors, current, identity, ct),
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
    ) =>
        (await access.ResolveChannelAsync(expected.Login, ct)).Match(
            resolved: resolved => resolved.Channel.Host.Id == expected.Id ? resolved.Channel : null,
            notFound: static _ => null
        );

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
