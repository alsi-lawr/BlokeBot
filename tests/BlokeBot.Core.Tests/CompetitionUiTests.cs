using AngleSharp.Dom;
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
using Bunit.TestDoubles;
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

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("RETAINED PRIVATE CUP"));
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
        UiTestContextFactory.AddMomentAttachmentServices(context, database);
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

    [Test]
    public async Task NewCompetition_OpensTheComposerWithFormatAndEntryExpanded()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCompetitionHostAsync(database);
        var service = CreateService(database);
        await SeedDraftAsync(service, hostId, "Existing cup");
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var page = context.Render<CompetitionsPage>();
        page.WaitForAssertion(() => _ = page.Find("[data-action='new-competition']"));
        page.Find("[data-action='new-competition']").Click();

        page.WaitForAssertion(() => _ = page.Find("[data-competition-create]"));
        Header(page, "competition-format").GetAttribute("aria-expanded").ShouldBe("true");
        Body(page, "competition-format").HasAttribute("inert").ShouldBeFalse();
        _ = page.Find("#competition-name");
        _ = page.Find("#competition-format");
        _ = page.Find("#competition-entry-kind");
        foreach (
            var stage in new[] { "competition-scoring", "competition-rewards", "competition-notes" }
        )
        {
            Header(page, stage).GetAttribute("aria-expanded").ShouldBe("false");
            Body(page, stage).HasAttribute("inert").ShouldBeTrue();
        }
    }

    [Test]
    public async Task EverySetupSection_ExpandsAndCollapsesFromItsOwnDisclosureHeader()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCompetitionHostAsync(database);
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(CreateService(database));

        var page = RenderComposer(context);

        foreach (var stage in _composerStages)
        {
            var header = Header(page, stage);
            header.GetAttribute("type").ShouldBe("button");
            header.HasAttribute("disabled").ShouldBeFalse();
            header.GetAttribute("aria-controls").ShouldBe(Body(page, stage).Id);
            var openAtRest = header.GetAttribute("aria-expanded") == "true";

            Header(page, stage).Click();

            page.WaitForAssertion(() =>
                Header(page, stage)
                    .GetAttribute("aria-expanded")
                    .ShouldBe(openAtRest ? "false" : "true")
            );
            Body(page, stage).HasAttribute("inert").ShouldBe(openAtRest);

            Header(page, stage).Click();

            page.WaitForAssertion(() =>
                Header(page, stage)
                    .GetAttribute("aria-expanded")
                    .ShouldBe(openAtRest ? "true" : "false")
            );
            Body(page, stage).HasAttribute("inert").ShouldBe(!openAtRest);
        }
    }

    [Test]
    public async Task ValidDraft_IsCreatedThroughTheServiceWithEverySectionsValues()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCompetitionHostAsync(database);
        var service = CreateService(database);
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var page = RenderComposer(context);
        page.Find("#competition-name").Input("Community Cup");
        page.Find("#competition-format").Change(nameof(CompetitionFormat.Tournament));
        page.Find("#competition-description").Input("A friendly league for regulars.");
        page.Find("#competition-entry-kind").Change(nameof(CompetitionEntryKind.Team));
        page.Find("#competition-capacity").Change("4");
        page.Find("#competition-team-size").Change("3");
        page.Find("#competition-seeding").Change(nameof(CompetitionSeeding.Seeded));
        page.Find("#competition-seed").Input("cup-seed");
        page.Find("#competition-minimum-points").Input("25");
        OpenStage(page, "competition-scoring");
        page.Find("#competition-tiebreak").Change(nameof(CompetitionTiebreak.ScoreForThenWins));
        page.Find("#competition-win-points").Change("5");
        page.Find("#competition-draw-points").Change("2");
        page.Find("#competition-loss-points").Change("1");
        OpenStage(page, "competition-rewards");
        page.Find("#competition-reminder").Change("12");
        page.Find("#competition-reminder-message").Input("Round {round} of {competition}.");
        page.Find("#competition-winner-points").Input("400");
        page.Find("#competition-runner-points").Input("200");
        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "+ Add milestone")
            .Click();
        page.FindAll(".competition-milestone-rule input")[0].Change("2");
        page.FindAll(".competition-milestone-rule input")[1].Change("75");
        OpenStage(page, "competition-notes");
        page.Find("#competition-lobby").Input("PRIVATE LOBBY");
        page.Find("#competition-create-note").Input("PRIVATE CREATE NOTE");
        page.Find("[data-action='create-competition']").Click();

        page.WaitForAssertion(() => _ = page.Find("[role='status']"));
        var created = (await service.GetModeratorAsync(hostId, default)).ShouldHaveSingleItem();
        created.Competition.Name.ShouldBe("Community Cup");
        created.Competition.Description.ShouldBe("A friendly league for regulars.");
        created.Competition.Format.ShouldBe(CompetitionFormat.Tournament);
        created.Competition.EntryKind.ShouldBe(CompetitionEntryKind.Team);
        created.Competition.Capacity.ShouldBe(4);
        created.Competition.TeamSize.ShouldBe(3);
        created.Competition.Seeding.ShouldBe(CompetitionSeeding.Seeded);
        created.Competition.Tiebreak.ShouldBe(CompetitionTiebreak.ScoreForThenWins);
        created.Competition.Seed.ShouldBe("cup-seed");
        created.Competition.Status.ShouldBe(CompetitionStatus.Draft);
        (
            created.Competition.WinPoints,
            created.Competition.DrawPoints,
            created.Competition.LossPoints
        ).ShouldBe((5, 2, 1));
        created.MinimumPoints.ShouldBe(new PointAmount(25));
        created.ReminderHoursBefore.ShouldBe(12);
        created.ReminderMessage.ShouldBe("Round {round} of {competition}.");
        created.WinnerPoints.ShouldBe(new PointAmount(400));
        created.RunnerUpPoints.ShouldBe(new PointAmount(200));
        created
            .MilestoneRewards.ShouldHaveSingleItem()
            .ShouldBe(new CompetitionMilestoneRewardView(2, new(75), string.Empty));
        created.PrivateLobbyInformation.ShouldBe("PRIVATE LOBBY");
        created.Audit.ShouldContain(audit => audit.PrivateReason == "PRIVATE CREATE NOTE");
    }

    [Test]
    [Arguments("competition-format", "format-and-entry")]
    [Arguments("competition-rewards", "rewards-and-reminders")]
    public async Task DraftFailure_StaysBesideTheSectionThatOwnsItAndPersistsNothing(
        string stage,
        string placement
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCompetitionHostAsync(database);
        var service = CreateService(database);
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var page = RenderComposer(context);
        if (placement == "rewards-and-reminders")
        {
            page.Find("#competition-name").Input("Community Cup");
            OpenStage(page, "competition-rewards");
            page.Find("#competition-reminder-message").Input(string.Empty);
        }
        page.Find("[data-action='create-competition']").Click();

        page.WaitForAssertion(() => _ = page.Find($"[data-composer-error='{placement}']"));
        var failure = page.Find($"[data-composer-error='{placement}']");
        failure.GetAttribute("role").ShouldBe("alert");
        _ = failure.Closest($"[data-stage='{stage}']").ShouldNotBeNull();
        Header(page, stage).GetAttribute("aria-expanded").ShouldBe("true");
        (await service.GetModeratorAsync(hostId, default)).ShouldBeEmpty();
    }

    [Test]
    public async Task BarePath_NormalizesToStandingsAndEveryWorkspaceIsAFragmentAnchor()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCompetitionHostAsync(database);
        var service = CreateService(database);
        await SeedDraftAsync(service, hostId, "Community Cup");
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/competitions");

        var page = RenderWorkspace(context);

        navigation.Uri.ShouldEndWith("/competitions#standings");
        navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
        var selected = page.Find("[aria-selected='true']");
        selected.TextContent.Trim().ShouldBe("Standings");
        selected.GetAttribute("tabindex").ShouldBe("0");
        var panel = page.Find("[role='tabpanel']");
        panel.Id.ShouldBe(selected.GetAttribute("aria-controls"));
        panel.GetAttribute("aria-labelledby").ShouldBe(selected.Id);
    }

    [Test]
    public async Task WorkspaceSelection_PushesOneHistoryEntryAndBackForwardFollowTheFragment()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCompetitionHostAsync(database);
        var service = CreateService(database);
        await SeedDraftAsync(service, hostId, "Community Cup");
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/competitions#standings");

        var page = RenderWorkspace(context);
        Tab(page, "Entrants").Click();

        page.WaitForAssertion(() => navigation.Uri.ShouldEndWith("/competitions#entrants"));
        navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
        page.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Entrants");
        _ = page.Find(".competition-entrant-list");

        navigation.NavigateTo("/competitions#standings");

        page.WaitForAssertion(() =>
            page.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Standings")
        );
        page.FindAll(".competition-entrant-list").ShouldBeEmpty();

        navigation.NavigateTo("/competitions#entrants");

        page.WaitForAssertion(() =>
            page.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Entrants")
        );
    }

    [Test]
    [Arguments("standings", "Standings")]
    [Arguments("schedule", "Schedule")]
    [Arguments("entrants", "Entrants")]
    [Arguments("settings", "Settings & history")]
    public async Task DirectFragmentLoad_OpensThatWorkspaceWithoutRewritingTheUrl(
        string fragment,
        string label
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCompetitionHostAsync(database);
        var service = CreateService(database);
        await SeedDraftAsync(service, hostId, "Community Cup");
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo($"/competitions#{fragment}");
        var depth = navigation.History.Count;

        var page = RenderWorkspace(context);

        navigation.Uri.ShouldEndWith($"/competitions#{fragment}");
        navigation.History.Count.ShouldBe(depth);
        var selected = page.Find("[aria-selected='true']");
        selected.TextContent.Trim().ShouldBe(label);
        page.Find("[role='tabpanel']").GetAttribute("aria-labelledby").ShouldBe(selected.Id);
    }

    [Test]
    public async Task SelectingAMatch_PublishesTheScheduleFragmentInsteadOfHidingBehindAStaleOne()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCompetitionHostAsync(database);
        var service = CreateService(database);
        var running = await SeedRunningCompetitionAsync(service, hostId);
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/competitions#standings");

        var page = RenderWorkspace(context);
        page.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Standings");
        var chosen = running.Matches.Last(match =>
            match.EntrantAId is not null && match.EntrantBId is not null
        );
        page.Find("#competition-result-match").Change(chosen.Id.Value.ToString());

        page.WaitForAssertion(() => navigation.Uri.ShouldEndWith("/competitions#schedule"));
        page.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Schedule");
        page.Find("[role='tabpanel']")
            .Id.ShouldBe(page.Find("[aria-selected='true']").GetAttribute("aria-controls"));
        _ = page.Find(".competition-fixture--selected");
    }

    private static readonly string[] _composerStages =
    [
        "competition-format",
        "competition-scoring",
        "competition-rewards",
        "competition-notes",
    ];

    private static CompetitionService CreateService(SqliteBlokeBotDbFactory database) =>
        new(
            database,
            TestEventBus.Create<AppEventKind>(),
            new NoopGrants(),
            [],
            TimeProvider.System
        );

    private static async Task<int> SeedCompetitionHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
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
        return host.Id;
    }

    private static async Task SeedDraftAsync(CompetitionService service, int hostId, string name) =>
        _ = (
            await service.CreateAsync(hostId, Draft(name), default)
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();

    private static async Task<CompetitionView> SeedRunningCompetitionAsync(
        CompetitionService service,
        int hostId
    )
    {
        await SeedDraftAsync(service, hostId, "Community Cup");
        var competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        _ = (
            await service.OpenRegistrationAsync(hostId, Transition(competition), default)
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();
        foreach (var login in new[] { "one", "two", "three", "four" })
        {
            competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
            _ = (
                await service.RegisterAsync(
                    hostId,
                    new(
                        Guid.NewGuid(),
                        competition.Id,
                        login,
                        null,
                        [new($"{login}-id", login, login, "private")],
                        Actor(),
                        "register"
                    ),
                    default
                )
            ).ShouldBeOfType<CompetitionOutcome.Succeeded>();
        }
        competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        _ = (
            await service.StartAsync(hostId, Transition(competition), DateTime.UtcNow, default)
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();
        return (await service.GetModeratorAsync(hostId, default)).Single().Competition;
    }

    private static CompetitionTransition Transition(CompetitionView competition) =>
        new(Guid.NewGuid(), competition.Id, competition.Revision, Actor(), "transition");

    private static CompetitionActor Actor() => new("host-id", "streamer");

    private static CompetitionDraft Draft(string name) =>
        new(
            Guid.NewGuid(),
            name,
            "Public description",
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
            "cup-seed",
            24,
            "Reminder: {competition} round {round} at {scheduled}. {public_url}",
            PointAmount.Zero,
            PointAmount.Zero,
            string.Empty,
            string.Empty,
            [],
            "PRIVATE LOBBY",
            Actor(),
            "create"
        );

    private static IRenderedComponent<CompetitionsPage> RenderComposer(BunitContext context)
    {
        var page = context.Render<CompetitionsPage>();
        page.WaitForAssertion(() => _ = page.Find("[data-competition-create]"));
        return page;
    }

    private static IRenderedComponent<CompetitionsPage> RenderWorkspace(BunitContext context)
    {
        var page = context.Render<CompetitionsPage>();
        page.WaitForAssertion(() => _ = page.Find("[role='tablist']"));
        return page;
    }

    private static IElement Tab(IRenderedComponent<CompetitionsPage> page, string label) =>
        page.FindAll("[role='tab']").Single(tab => tab.TextContent.Trim() == label);

    private static IElement Header(IRenderedComponent<CompetitionsPage> page, string stage) =>
        page.Find($"[data-stage='{stage}'] .studio-stage__header");

    private static IElement Body(IRenderedComponent<CompetitionsPage> page, string stage) =>
        page.Find($"#{Header(page, stage).GetAttribute("aria-controls")}");

    private static void OpenStage(IRenderedComponent<CompetitionsPage> page, string stage)
    {
        Header(page, stage).Click();
        page.WaitForAssertion(() =>
            Header(page, stage).GetAttribute("aria-expanded").ShouldBe("true")
        );
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
