using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.BlokeRaid;
using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Collectives;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

internal sealed class ViewerPortalTestContext
{
    internal ViewerPortalTestContext(
        SqliteBlokeBotDbFactory database,
        IDbContextFactory<BlokeBotDbContext>? bingoDatabase = null
    )
    {
        Database = database;
        Events = TestEventBus.Create<AppEventKind>();
        var events = Events;
        Changes = new(events);
        Bounties = new(database, events, Clock);
        Collectives = new(database, new UnavailableRaidProvider(), Clock);
        Queues = new(database, events, Clock);
        Requests = new(database, events, Clock);
        Moments = new(database, new ClipProvider(database), events, Clock);
        var points = new PointBalanceService(database);
        Passports = new(database, points, new OfflineStream(), Clock);
        Access = new(new PublicLeaderboardHostLookup(database), Passports, database);
        var community = new CommunityProgressionService(database, events, Clock);
        Bingo = new BingoService(bingoDatabase ?? database, community, events, Clock);
        var activities = new PortalActivityProjectors(
            Bingo,
            new BlokeRaidService(database, events, new BlokeRaidRandom(), Clock),
            Bounties,
            new CompetitionService(database, events, community, [], Clock),
            community,
            Moments,
            Clock
        );
        Telemetry = new(
            Clock,
            new DurableAlertService(database, Clock, events),
            NullLogger<PortalReadTelemetry>.Instance
        );
        Catalogue = new(
            Access,
            new PortalProjectors(
                activities,
                new PortalDirectoryProjectors(Queues, Requests, Collectives),
                new PortalPersonalProjectors(
                    points,
                    new GuessingHistoryService(database),
                    Passports,
                    Access
                )
            ),
            Scheduler,
            new PortalProjectionRunner(Telemetry)
        );
    }

    internal BlokeBot.Eventing.EventBus<AppEventKind> Events { get; }
    internal PortalReadTelemetry Telemetry { get; }
    internal PortalReadScheduler Scheduler { get; } = new();
    internal BingoService Bingo { get; }
    internal SqliteBlokeBotDbFactory Database { get; }
    internal PortalClock Clock { get; } =
        new(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
    internal HostedChannelChangeNotifier Changes { get; }
    internal BountyService Bounties { get; }
    internal CollectiveService Collectives { get; }
    internal PlayQueueService Queues { get; }
    internal RequestBoardService Requests { get; }
    internal MomentHubService Moments { get; }
    internal ViewerPassportService Passports { get; }
    internal ViewerPortalAccess Access { get; }
    internal ViewerPortalCatalogueService Catalogue { get; }

    internal async Task<int> HostAsync(string login, HostFeatureFlags features)
    {
        await using var db = await Database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            EnabledFeatures = features,
            CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    internal async Task<PortalChannel> ChannelAsync(string login) =>
        (await Access.ResolveChannelAsync(login, default))
            .ShouldBeOfType<PortalChannelOutcome.Resolved>()
            .Channel;

    internal async Task PointsAsync(int hostId, string login, string amount)
    {
        await using var db = await Database.CreateDbContextAsync();
        _ = db.PointBalances.Add(
            new PointBalance
            {
                HostId = hostId,
                Login = login,
                Amount = amount,
                UpdatedAtUtc = Clock.GetUtcNow().UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    internal sealed class PortalClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class ClipProvider(SqliteBlokeBotDbFactory database) : IMomentProviderOperations
    {
        public async Task<MomentProviderOutcome> CaptureAsync(
            int hostId,
            Guid publicId,
            bool markerFallbackEnabled,
            string description,
            CancellationToken ct
        )
        {
            await using var db = await database.CreateDbContextAsync(ct);
            var clip = new TwitchClip
            {
                HostId = hostId,
                IdempotencyKey = publicId.ToString("N"),
                Status = TwitchClipStatus.Available,
                FinalUrl = $"https://clips.twitch.tv/{publicId:N}",
            };
            _ = db.TwitchClips.Add(clip);
            _ = await db.SaveChangesAsync(ct);
            return new MomentProviderOutcome.ClipReady(clip.Id);
        }
    }

    private sealed class OfflineStream : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(static _ =>
                ValueTask.FromResult(
                    Result<HostStreamLivenessOutcome, Never>.Success(
                        new HostStreamLivenessOutcome.Offline()
                    )
                )
            );
    }

    private sealed class UnavailableRaidProvider : IRaidCollaborationProvider
    {
        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
            );

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelByIdAsync(
            int hostId,
            string twitchUserId,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
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
                new ConfirmedRaidStartOutcome.ProviderRejected()
            );

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);
    }
}
