using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Bunit;
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
}
