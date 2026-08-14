using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Simulation;

internal static class SimulationServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotSimulation(this IServiceCollection services)
    {
        _ = services.Replace(
            ServiceDescriptor.Singleton<TimeProvider>(new SimulationTimeProvider())
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<
                IRaidCollaborationProvider,
                SimulationRaidCollaborationProvider
            >()
        );
        _ = services.AddSingleton<SimulationCommandCatalogScenario>();
        _ = services.Replace(
            ServiceDescriptor.Singleton<IHostStreamLivenessProvider>(static provider =>
                provider.GetRequiredService<SimulationCommandCatalogScenario>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IPointTargetUserLookup, SimulationPointTargetUserLookup>()
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<
                IAutomaticRaidShoutoutDelivery,
                SimulationAutomaticRaidShoutoutDelivery
            >()
        );
        _ = services.AddSingleton<SimulationNativeTwitchDashboardOperations>();
        _ = services.Replace(
            ServiceDescriptor.Singleton<IShoutoutDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IPollDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IClipMarkerDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IChannelPointsDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IPredictionDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.AddSingleton<SimulationFixtureSeeder>();
        _ = services.AddSingleton<SimulationReadiness>();
        _ = services.AddSingleton<SimulationStartupCoordinator>();
        return services;
    }

    private sealed class SimulationAutomaticRaidShoutoutDelivery : IAutomaticRaidShoutoutDelivery
    {
        public Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
            AutomaticRaidShoutoutDeliveryRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<AutomaticRaidShoutoutDeliveryResult>(
                new AutomaticRaidShoutoutDeliveryResult.Delivered()
            );
    }

    private sealed class SimulationTimeProvider : TimeProvider
    {
        private readonly long _startedAtTimestamp = TimeProvider.System.GetTimestamp();

        public override DateTimeOffset GetUtcNow() =>
            SimulationMode.Now + TimeProvider.System.GetElapsedTime(_startedAtTimestamp);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        ) => TimeProvider.System.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class SimulationPointTargetUserLookup : IPointTargetUserLookup
    {
        public Task<bool> ExistsAsync(string login, CancellationToken ct) =>
            Task.FromResult(!string.IsNullOrWhiteSpace(login));
    }

    private sealed class SimulationRaidCollaborationProvider(TimeProvider clock)
        : IRaidCollaborationProvider
    {
        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                login switch
                {
                    "maplepixel" => new RaidChannelSnapshotOutcome.Available(
                        new(
                            "maple-id",
                            "maplepixel",
                            "MaplePixel",
                            "maple-stream",
                            "Celeste",
                            "en",
                            "Golden berries and good company",
                            126,
                            new(
                                "maple-clip",
                                "https://clips.twitch.tv/MapleClip",
                                "Golden berry, finally",
                                clock.GetUtcNow().AddDays(-4),
                                27.4m
                            )
                        )
                    ),
                    "cozyworkshop" => new RaidChannelSnapshotOutcome.Available(
                        new(
                            "cozy-id",
                            "cozyworkshop",
                            "CozyWorkshop",
                            "cozy-stream",
                            "Makers & Crafting",
                            "en",
                            "Building a tiny arcade cabinet",
                            74,
                            null
                        )
                    ),
                    _ => new RaidChannelSnapshotOutcome.Offline(login),
                }
            );

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelByIdAsync(
            int hostId,
            string twitchUserId,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            LoadLiveChannelAsync(
                hostId,
                twitchUserId switch
                {
                    "maple-id" => "maplepixel",
                    "cozy-id" => "cozyworkshop",
                    _ => twitchUserId,
                },
                approvedClipId,
                cancellationToken
            );

        public Task<FollowedLiveChannelsOutcome> LoadFollowedLiveChannelsAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<FollowedLiveChannelsOutcome>(
                new FollowedLiveChannelsOutcome.Unavailable()
            );

        public Task<bool> HasFollowedLiveAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);

        public Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
            int hostId,
            string targetTwitchUserId,
            string targetLogin,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ConfirmedRaidStartOutcome>(
                new ConfirmedRaidStartOutcome.Started(targetLogin)
            );

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(true);
    }
}
