using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferCoordinatorTests
{
    [Test]
    public async Task MultiSectionFailure_RollsBackFeatureGraphsEnablementActivationAndAudit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(
            new FailImportAuditSaveInterceptor()
        );
        var hostId = await SeedHostAsync(database, "destination");
        var observer = new AuditObserver(database, ConfigurationSectionId.Points);
        var coordinator = Coordinator(database, observer);
        var selection = Selection(
            hostId,
            ConfigurationSectionId.CustomCommands,
            ConfigurationSectionId.Points,
            ConfigurationSectionId.ChannelToolEnablement
        ) with
        {
            EnablementChanges = new HashSet<HostFeatureFlags> { HostFeatureFlags.Polls },
        };

        var outcome = await coordinator.ApplyAsync(
            Session(hostId),
            Document(
                commands: Commands(),
                points: Points(),
                enablement: ChannelToolEnablementMapper.FromFlags(HostFeatureFlags.Polls)
            ),
            selection,
            new("actor-id", "destination"),
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Failed>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCommands.CountAsync()).ShouldBe(0);
        (await verify.CustomMessageLibraryEntries.CountAsync()).ShouldBe(0);
        (await verify.PointsSettings.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationActivations.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        (await verify.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.None);
        observer.Calls.ShouldBe(0);
    }

    [Test]
    public async Task SuccessfulImport_RemapReferencesPreserveRuntimeAndWriteBoundedAuditAndPendingActivation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination");
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "viewer",
                    Amount = "42",
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var selection = Selection(
            hostId,
            ConfigurationSectionId.CustomCommands,
            ConfigurationSectionId.Points,
            ConfigurationSectionId.ChannelToolEnablement
        ) with
        {
            EnablementChanges = new HashSet<HostFeatureFlags> { HostFeatureFlags.Polls },
        };

        var outcome = await Coordinator(database)
            .ApplyAsync(
                Session(hostId),
                Document(
                    commands: Commands(),
                    points: Points(),
                    enablement: ChannelToolEnablementMapper.FromFlags(HostFeatureFlags.Polls)
                ),
                selection,
                new("actor-id", "destination"),
                CancellationToken.None
            );

        var applied = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>().Result;
        _ = applied.ActivationId.ShouldNotBeNull();
        await using var verify = await database.CreateDbContextAsync();
        var command = await verify.CustomCommands.Include(x => x.Action).SingleAsync();
        var reply = await verify.CustomMessageLibraryEntries.SingleAsync();
        command.Action.ZeroArgumentMessageLibraryEntryId.ShouldBe(reply.Id);
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("42");
        (await verify.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.Polls);
        (await verify.ConfigurationActivations.SingleAsync()).Status.ShouldBe(
            ConfigurationActivationStatus.Pending
        );
        var audit = await verify.ConfigurationImportAudits.SingleAsync();
        audit.OperationId.ShouldBe(applied.OperationId);
        audit.ActorTwitchUserId.ShouldBe("actor-id");
        audit.SummaryJson.ShouldBe(
            "{\"Sections\":[{\"Id\":\"channelToolEnablement\",\"Count\":1},{\"Id\":\"customCommands\",\"Count\":3},{\"Id\":\"points\",\"Count\":1}]}"
        );
        audit.SummaryJson.ShouldNotContain("hello");
        audit.SummaryJson.Length.ShouldBeLessThanOrEqualTo(2048);
    }

    [Test]
    public async Task ConfigurationOnlyImport_RemainsDisabledAndCreatesNoActivation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination");

        var outcome = await Coordinator(database)
            .ApplyAsync(
                Session(hostId),
                Document(commands: Commands()),
                Selection(hostId, ConfigurationSectionId.CustomCommands),
                new("actor-id", "destination"),
                CancellationToken.None
            );

        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.None);
        (await verify.ConfigurationActivations.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ImportObserver_RunsOnlyAfterCommittedAuditIsVisible()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination");
        var observer = new AuditObserver(database, ConfigurationSectionId.Points);

        var outcome = await Coordinator(database, observer)
            .ApplyAsync(
                Session(hostId),
                Document(points: Points()),
                Selection(hostId, ConfigurationSectionId.Points),
                new("actor-id", "destination"),
                CancellationToken.None
            );

        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        observer.Calls.ShouldBe(1);
        observer.SawCommittedAudit.ShouldBeTrue();
    }

    [Test]
    public async Task AddMissingExistingPoints_ReportsSkippedSectionInsteadOfChangedSection()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination");
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointsSettings.Add(new PointsSettings { HostId = hostId });
            _ = await seed.SaveChangesAsync();
        }
        var selection = new ConfigurationImportSelection(
            hostId,
            [new(ConfigurationSectionId.Points, ImportConflictStrategy.AddMissing, [])],
            new HashSet<HostFeatureFlags>()
        );

        var outcome = await Coordinator(database)
            .ApplyAsync(
                Session(hostId),
                Document(points: Points()),
                selection,
                new("actor-id", "destination"),
                CancellationToken.None
            );

        var applied = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>().Result;
        applied.ChangedSections.ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ConfigurationImportAudits.SingleAsync()).SummaryJson.ShouldBe(
            "{\"Sections\":[]}"
        );
    }

    [Test]
    public async Task GuessingAndPointsSharedAlias_ApplyingTogether_RejectsAndRollsBack()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination");

        var outcome = await Coordinator(database)
            .ApplyAsync(
                Session(hostId),
                Document(guessing: Guessing("shared"), points: Points("shared")),
                Selection(hostId, ConfigurationSectionId.Guessing, ConfigurationSectionId.Points),
                new("actor-id", "destination"),
                CancellationToken.None
            );

        outcome
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>()
            .Issues.ShouldContain(issue =>
                issue.Message == "!shared is already used by another bot command."
            );
        await using var verify = await database.CreateDbContextAsync();
        (await verify.Profiles.CountAsync()).ShouldBe(0);
        (await verify.PointsSettings.CountAsync()).ShouldBe(0);
        (await verify.CommandAliases.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task CustomAndGuessingSharedAlias_ApplyingTogether_RejectsAndRollsBack()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination");

        var outcome = await Coordinator(database)
            .ApplyAsync(
                Session(hostId),
                Document(commands: Commands("shared"), guessing: Guessing("shared")),
                Selection(
                    hostId,
                    ConfigurationSectionId.CustomCommands,
                    ConfigurationSectionId.Guessing
                ),
                new("actor-id", "destination"),
                CancellationToken.None
            );

        outcome
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>()
            .Issues.ShouldContain(issue =>
                issue.Message == "!shared is already used by another bot command."
            );
        await using var verify = await database.CreateDbContextAsync();
        (await verify.Profiles.CountAsync()).ShouldBe(0);
        (await verify.CustomCommands.CountAsync()).ShouldBe(0);
        (await verify.CommandAliases.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task SelectedHostMismatch_IsRejectedWithoutMutation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var destinationId = await SeedHostAsync(database, "destination");
        var otherId = await SeedHostAsync(database, "other");

        var outcome = await Coordinator(database)
            .ApplyAsync(
                Session(otherId, "other"),
                Document(commands: Commands()),
                Selection(destinationId, ConfigurationSectionId.CustomCommands),
                new("actor-id", "other"),
                CancellationToken.None
            );

        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Rejected>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCommands.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task OppositePendingEnablementChanges_CoalesceToNoRuntimeTransition()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination");
        var coordinator = Coordinator(database);
        var selection = Selection(hostId, ConfigurationSectionId.ChannelToolEnablement) with
        {
            EnablementChanges = new HashSet<HostFeatureFlags> { HostFeatureFlags.Polls },
        };

        var enabled = await coordinator.ApplyAsync(
            Session(hostId),
            Document(enablement: ChannelToolEnablementMapper.FromFlags(HostFeatureFlags.Polls)),
            selection,
            new("actor-id", "destination"),
            CancellationToken.None
        );
        var disabled = await coordinator.ApplyAsync(
            Session(hostId),
            Document(enablement: ChannelToolEnablementMapper.FromFlags(HostFeatureFlags.None)),
            selection,
            new("actor-id", "destination"),
            CancellationToken.None
        );

        var firstId = enabled
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>()
            .Result.ActivationId;
        disabled
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>()
            .Result.ActivationId.ShouldBe(firstId);
        await using var verify = await database.CreateDbContextAsync();
        var activation = await verify.ConfigurationActivations.SingleAsync();
        activation.EnabledChanges.ShouldBe(HostFeatureFlags.None);
        activation.DisabledChanges.ShouldBe(HostFeatureFlags.None);
        (await verify.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.None);
    }

    private static ConfigurationTransferCoordinator Coordinator(
        SqliteBlokeBotDbFactory database,
        IConfigurationImportObserver? observer = null
    )
    {
        var writer = new CustomCommandConfigurationGraphWriter(
            database,
            null!,
            TimeProvider.System
        );
        var customCommands = new CustomCommandConfigurationTransferAdapter(
            writer,
            new CustomCommandAliasRegistry(),
            TimeProvider.System
        );
        return observer is null
            ? new(
                database,
                customCommands,
                new GrantedAuthority(),
                new ConfigurationActivationQueue(),
                TimeProvider.System,
                NullLogger<ConfigurationTransferCoordinator>.Instance
            )
            : new(
                database,
                customCommands,
                new GrantedAuthority(),
                new ConfigurationActivationQueue(),
                TimeProvider.System,
                NullLogger<ConfigurationTransferCoordinator>.Instance,
                new(database),
                UnavailableOverlayConfigurationTransferAdapter.Instance,
                UnavailableAutomationConfigurationTransferAdapter.Instance,
                new ConfigurationImportObserverDispatcher(
                    [observer],
                    NullLogger<ConfigurationImportObserverDispatcher>.Instance
                ),
                new(1, 1)
            );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static AuthenticatedSession Session(int hostId, string login = "destination")
    {
        var host = new BotHostChoice(hostId, login, login, AuthRole.Streamer);
        return new()
        {
            IsAuthenticated = true,
            UserId = $"{login}-id",
            Login = login,
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private static ConfigurationImportSelection Selection(
        int hostId,
        params ConfigurationSectionId[] sections
    ) =>
        new(
            hostId,
            sections
                .Select(x => new SectionImportSelection(x, ImportConflictStrategy.Merge, []))
                .ToArray(),
            new HashSet<HostFeatureFlags>()
        );

    private static ConfigurationDocumentV1 Document(
        CustomCommandsSectionV1? commands = null,
        GuessingSectionV1? guessing = null,
        PointsSectionV1? points = null,
        ChannelToolEnablementV1? enablement = null
    ) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.UtcNow,
            new("source-channel", "0.12.0"),
            new(commands, Guessing: guessing, Points: points, ChannelToolEnablement: enablement)
        );

    private static CustomCommandsSectionV1 Commands(string alias = "hello-transfer") =>
        new(
            "UTC",
            [new("reply-0001", "hello reply", CustomMessageSelectionMode.Sequential, ["Hello!"])],
            [],
            [
                new(
                    "command-0001",
                    "hello command",
                    true,
                    [alias],
                    true,
                    true,
                    0,
                    CustomCommandCooldownScope.User,
                    CustomCommandInvocationLimit.Unlimited,
                    new(CustomCommandActionTypeV1.Message, ZeroArgumentReplyId: "reply-0001")
                ),
            ]
        );

    private static GuessingSectionV1 Guessing(string alias) =>
        new([
            new(
                "profile-0001",
                "Imported",
                "imported",
                true,
                "0",
                [new(AppCommandKind.Guess, [alias])],
                new("", "", "", "", "", "", "", "", "", "", "", "", ""),
                [new("answer", "answer", ReplyDeliveryTarget.Chat)]
            ),
        ]);

    private static PointsSectionV1 Points(string? alias = null)
    {
        var value = new PointsSettings();
        return new(
            value.PointLabel,
            alias is null ? [] : [new(AppCommandKind.Points, [alias])],
            new(
                value.BalanceReply,
                value.OtherBalanceReply,
                value.TransferReply,
                value.AddReply,
                value.RemoveReply,
                value.InvalidAmountReply,
                value.InsufficientBalanceReply,
                value.ModeratorOnlyReply,
                value.GamblingWinReply,
                value.GamblingLoseReply,
                value.GiveawayStartedReply,
                value.GiveawayUpdateReply,
                value.GiveawayJoinedReply,
                value.GiveawayAlreadyJoinedReply,
                value.GiveawayEndedReply,
                value.GiveawayNoEntrantsReply,
                value.GiveawayCancelledReply,
                value.GiveawayAlreadyActiveReply,
                value.GiveawayNotActiveReply,
                value.GiveawayCooldownReply,
                value.StreamOfflineReply,
                value.NotEligibleReply,
                value.FollowerEligibilityUnavailableReply
            ),
            value.GamblingWinRatePercent,
            value.GamblingCooldownSeconds,
            value.GiveawayDurationSeconds,
            value.GiveawayMinimumPayout,
            value.GiveawayMaximumPayout,
            value.GiveawayWinnerCount,
            value.GiveawayEligibility,
            value.GiveawayCooldownSeconds
        );
    }

    private sealed class GrantedAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }

    private sealed class AuditObserver(
        SqliteBlokeBotDbFactory database,
        ConfigurationSectionId section
    ) : IConfigurationImportObserver
    {
        public ConfigurationSectionId Section => section;

        public int Calls { get; private set; }

        public bool SawCommittedAudit { get; private set; }

        public async ValueTask ImportedAsync(int hostId, CancellationToken cancellationToken)
        {
            Calls++;
            await using var db = await database.CreateDbContextAsync(cancellationToken);
            SawCommittedAudit = await db.ConfigurationImportAudits.AnyAsync(
                value => value.HostId == hostId,
                cancellationToken
            );
        }
    }

    private sealed class FailImportAuditSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            eventData.Context?.ChangeTracker.Entries<ConfigurationImportAudit>().Any() == true
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("Planned import commit failure.")
                )
                : ValueTask.FromResult(result);
    }
}
