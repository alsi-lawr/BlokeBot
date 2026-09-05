using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

public static class ViewerPortalCatalogue
{
    public static ImmutableArray<PortalFeatureDescriptor> Descriptors { get; } = Create();

    internal static IReadOnlyList<HostFeatureFlags> PublicFeatures(HostFeatureFlags enabled) =>
        HostFeatureCatalog
            .Features.Where(feature =>
                enabled.Contains(feature)
                && Descriptors.Any(descriptor => descriptor.Feature == feature)
            )
            .ToImmutableArray();

    private static ImmutableArray<PortalFeatureDescriptor> Create()
    {
        ImmutableArray<PortalFeatureDescriptor> descriptors =
        [
            Channel(
                HostFeatureFlags.Bingo,
                PortalIcon.Bingo,
                PortalCategory.Activity,
                "bingo",
                static (services, channel, route, ct) =>
                    services.Activities.BingoAsync(channel, route, ct)
            ),
            Slugged(
                HostFeatureFlags.PlayWithViewers,
                PortalIcon.Queue,
                "queues",
                static (services, channel, route, ct) =>
                    services.Directories.QueuesAsync(channel, route, ct)
            ),
            Channel(
                HostFeatureFlags.Bounties,
                PortalIcon.Bounty,
                PortalCategory.Activity,
                "bounties",
                static (services, channel, route, ct) =>
                    services.Activities.BountiesAsync(channel, route, ct),
                PortalFallback.OwnerAdmission
            ),
            Channel(
                HostFeatureFlags.CooperativeGame,
                PortalIcon.Raid,
                PortalCategory.Activity,
                "raid",
                static (services, channel, route, ct) =>
                    services.Activities.RaidAsync(channel, route, ct)
            ),
            Channel(
                HostFeatureFlags.Competitions,
                PortalIcon.Competition,
                PortalCategory.Activity,
                "competitions",
                static (services, channel, route, ct) =>
                    services.Activities.CompetitionsAsync(channel, route, ct)
            ),
            Channel(
                HostFeatureFlags.CommunityProgression,
                PortalIcon.Community,
                PortalCategory.Community,
                "community",
                static (services, channel, route, ct) =>
                    services.Activities.CommunityAsync(channel, route, ct)
            ),
            Slugged(
                HostFeatureFlags.RequestBoards,
                PortalIcon.Request,
                "requests",
                static (services, channel, route, ct) =>
                    services.Directories.RequestsAsync(channel, route, ct)
            ),
            Channel(
                HostFeatureFlags.Moments,
                PortalIcon.Moment,
                PortalCategory.Community,
                "moments",
                static (services, channel, route, ct) =>
                    services.Activities.MomentsAsync(channel, route, ct)
            ),
            Channel(
                HostFeatureFlags.Points,
                PortalIcon.Points,
                PortalCategory.Leaderboard,
                "points/leaderboard",
                static (services, channel, route, ct) =>
                    services.Personal.PointsAsync(channel, route, ct)
            ),
            Channel(
                HostFeatureFlags.Guessing,
                PortalIcon.Guessing,
                PortalCategory.Leaderboard,
                "guessing/leaderboard",
                static (services, channel, route, ct) =>
                    services.Personal.GuessingAsync(channel, route, ct)
            ),
            new(
                HostFeatureFlags.Collectives,
                PortalIcon.Collective,
                PortalCategory.Community,
                PortalAudience.Public,
                static (services, channel, _, ct) =>
                    services.Directories.CollectivesAsync(
                        channel,
                        id => $"/collectives/{Segment(channel.Host.Login)}/{id.Value:D}",
                        ct
                    )
            ),
            new(
                HostFeatureFlags.ViewerPassports,
                PortalIcon.Passport,
                PortalCategory.Personal,
                PortalAudience.Self,
                static (services, channel, identity, ct) =>
                    services.Personal.PassportAsync(
                        channel,
                        identity,
                        PassportRoute(channel.Host),
                        ct
                    ),
                PassportRoute
            ),
        ];
        _ = descriptors.ToDictionary(descriptor => descriptor.Feature);
        return descriptors;
    }

    private static PortalFeatureDescriptor Channel(
        HostFeatureFlags feature,
        PortalIcon icon,
        PortalCategory category,
        string prefix,
        Func<
            PortalProjectors,
            PortalChannel,
            string,
            CancellationToken,
            Task<PortalSummaryOutcome>
        > project,
        PortalFallback fallback = PortalFallback.ChannelRoute
    ) =>
        new(
            feature,
            icon,
            category,
            PortalAudience.Public,
            (services, channel, _, ct) =>
                project(services, channel, ChannelRoute(prefix, channel.Host), ct),
            fallback switch
            {
                PortalFallback.ChannelRoute => host => ChannelRoute(prefix, host),
                PortalFallback.OwnerAdmission => null,
            }
        );

    private static PortalFeatureDescriptor Slugged(
        HostFeatureFlags feature,
        PortalIcon icon,
        string prefix,
        Func<
            PortalProjectors,
            PortalChannel,
            Func<string, string>,
            CancellationToken,
            Task<PortalSummaryOutcome>
        > project
    ) =>
        new(
            feature,
            icon,
            PortalCategory.Activity,
            PortalAudience.Public,
            (services, channel, _, ct) =>
                project(
                    services,
                    channel,
                    slug => $"/{prefix}/{Segment(channel.Host.Login)}/{Segment(slug)}",
                    ct
                )
        );

    private enum PortalFallback
    {
        ChannelRoute,
        OwnerAdmission,
    }

    private static string ChannelRoute(string prefix, PortalHostKey host) =>
        $"/{prefix}/{Segment(host.Login)}";

    private static string PassportRoute(PortalHostKey host) =>
        $"/passports/{Segment(host.Login)}/me";

    private static string Segment(string value) => Uri.EscapeDataString(value);
}
