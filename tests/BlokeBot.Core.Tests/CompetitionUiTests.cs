using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CompetitionUiTests
{
    [Test]
    public async Task SignedInDirectRoute_WhenDisabled_ShowsRecoveryWithoutRetainedData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.None,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            _ = db.Competitions.Add(
                new Competition
                {
                    HostId = hostId,
                    PublicId = Guid.NewGuid(),
                    CreationOperationId = Guid.NewGuid(),
                    Name = "RETAINED PRIVATE CUP",
                    Format = CompetitionFormat.RoundRobin,
                    EntryKind = CompetitionEntryKind.Individual,
                    Status = CompetitionStatus.Registration,
                    Seeding = CompetitionSeeding.Random,
                    Tiebreak = CompetitionTiebreak.ScoreDifferenceThenScoreFor,
                    Capacity = 8,
                    TeamSize = 1,
                    Seed = "seed",
                    AlgorithmVersion = "v1",
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var service = new CompetitionService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new NoopGrants(),
            [],
            TimeProvider.System
        );
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var cut = context.Render<CompetitionsPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-competitions-disabled-recovery]")
                .TextContent.ShouldContain("Channel setup");
            cut.Markup.ShouldContain("without replaying suppressed commands");
            cut.Markup.ShouldNotContain("RETAINED PRIVATE CUP");
        });
    }

    [Test]
    public async Task PublicResults_ShowAuthoritativeStateWithoutPrivateContactLobbyOrAudit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Competitions,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var service = new CompetitionService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new NoopGrants(),
            [],
            TimeProvider.System
        );
        _ = (
            await service.CreateAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    "Public Cup",
                    "Public summary",
                    CompetitionFormat.RoundRobin,
                    CompetitionEntryKind.Individual,
                    CompetitionSeeding.Random,
                    CompetitionTiebreak.ScoreDifferenceThenScoreFor,
                    8,
                    1,
                    PointAmount.Zero,
                    3,
                    1,
                    0,
                    "public-seed",
                    24,
                    "Reminder: {competition} round {round} at {scheduled}. {public_url}",
                    PointAmount.Zero,
                    PointAmount.Zero,
                    string.Empty,
                    string.Empty,
                    [],
                    "PRIVATE LOBBY",
                    new("host-id", "streamer"),
                    "PRIVATE AUDIT"
                ),
                default
            )
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();
        var competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        _ = (
            await service.OpenRegistrationAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    competition.Id,
                    competition.Revision,
                    new("host-id", "streamer"),
                    "PRIVATE OPEN NOTE"
                ),
                default
            )
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();
        competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        _ = (
            await service.RegisterAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    competition.Id,
                    "Viewer Name",
                    null,
                    [new("viewer-id", "viewer", "Viewer Name", "CONTACT-SECRET-42")],
                    new("viewer-id", "viewer"),
                    "REGISTRATION-SECRET-42"
                ),
                default
            )
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();

        using var context = new BunitContext();
        _ = context.Services.AddSingleton(service);
        _ = context.AddAuthorization().SetNotAuthorized();
        var cut = context.Render<PublicCompetitionsPage>(parameters =>
            parameters.Add(page => page.Channel, "streamer")
        );

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Public Cup");
            cut.Markup.ShouldContain("Viewer Name");
            cut.Markup.ShouldContain("public-seed");
            cut.Markup.ShouldNotContain("PRIVATE LOBBY");
            cut.Markup.ShouldNotContain("CONTACT-SECRET-42");
            cut.Markup.ShouldNotContain("PRIVATE AUDIT");
            cut.Markup.ShouldNotContain("REGISTRATION-SECRET-42");
        });
    }

    [Test]
    public async Task SelectedHostChangesAfterLoad_ManagementMutationPersistsNothing()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        int otherHostId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Competitions,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var other = new BotHost
            {
                TwitchUserId = "other-id",
                Login = "other",
                DisplayName = "Other",
                EnabledFeatures = HostFeatureFlags.Competitions,
                CreatedAtUtc = DateTime.UtcNow,
            };
            seed.Hosts.AddRange(host, other);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            otherHostId = other.Id;
            _ = seed.Competitions.Add(
                new Competition
                {
                    HostId = hostId,
                    PublicId = Guid.NewGuid(),
                    CreationOperationId = Guid.NewGuid(),
                    Name = "Stale selected cup",
                    Format = CompetitionFormat.RoundRobin,
                    EntryKind = CompetitionEntryKind.Individual,
                    Status = CompetitionStatus.Draft,
                    Seeding = CompetitionSeeding.Random,
                    Tiebreak = CompetitionTiebreak.ScoreDifferenceThenScoreFor,
                    Capacity = 8,
                    TeamSize = 1,
                    Seed = "stale-host",
                    AlgorithmVersion = CompetitionSchedule.AlgorithmVersion,
                    ReminderMessage = "Reminder",
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var service = new CompetitionService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new NoopGrants(),
            [],
            TimeProvider.System
        );
        var fixture = UiTestContextFactory.CreateWithAuthorization(database, hostId);
        using var context = fixture.Context;
        _ = context.Services.AddSingleton(service);
        _ = context.Services.AddSingleton(
            new ModeratorAuthorityService(
                new UnavailableAppTokens(),
                new HelixClient(
                    new ThrowingHttpClientFactory(),
                    global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
                ),
                BotSettings.FromOptions(
                    new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
                ),
                new HostModAccessService(
                    database,
                    new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
                ),
                TimeProvider.System
            )
        );
        var cut = context.Render<CompetitionsPage>();
        _ = cut.WaitForElement("button[data-action='open-registration']");
        var otherChoice = new BotHostChoice(otherHostId, "other", "Other", AuthRole.Streamer);
        _ = fixture.Authorization.SetClaims(
            TestPrincipals
                .BlokeBotUser(
                    "other",
                    role: AuthRole.Streamer,
                    availableHosts: [otherChoice],
                    selectedHost: otherChoice
                )
                .Claims.ToArray()
        );

        await cut.Find("button[data-action='open-registration']").ClickAsync(new());

        await using var verify = await database.CreateDbContextAsync();
        (await verify.Competitions.SingleAsync(x => x.HostId == hostId)).Status.ShouldBe(
            CompetitionStatus.Draft
        );
        (await verify.CompetitionAudits.CountAsync()).ShouldBe(0);
        (await verify.CompetitionEvents.CountAsync()).ShouldBe(0);
    }

    private sealed class NoopGrants : ICommunityAchievementGrantService
    {
        public Task<CommunityExternalGrantOutcome> GrantAsync(
            CommunityExternalGrantRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<CommunityExternalGrantOutcome>(
                new CommunityExternalGrantOutcome.Granted(Guid.NewGuid(), false)
            );
    }

    private sealed class UnavailableAppTokens : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Authority mismatch must not call Twitch.");
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new ThrowingHandler(), disposeHandler: true);

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) =>
                Task.FromException<HttpResponseMessage>(
                    new InvalidOperationException("Authority mismatch must not call Twitch.")
                );
        }
    }
}
