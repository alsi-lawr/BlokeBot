using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomAnnouncementFeatureTransitionTests : CustomAnnouncementSchedulerTestBase
{
    private static readonly DateTimeOffset _disabledAt = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _enabledAt = _disabledAt.AddHours(2);

    [Test]
    public async Task NormalReEnable_AdvancesOccurrenceBoundaryWithoutChangingSentHistory()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_disabledAt);
        var hostId = await SeedHostAsync(
            database,
            "streamer",
            changedAtUtc: _disabledAt.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
            database,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 120 },
            ["Interval"],
            _disabledAt.AddHours(-1).UtcDateTime
        );
        var features = FeatureService(database, clock);

        _ = await features.DisableAsync(
            hostId,
            HostFeatureFlags.CustomCommands,
            CancellationToken.None
        );
        clock.SetUtcNow(_enabledAt);
        _ = await features.EnableAsync(
            hostId,
            HostFeatureFlags.CustomCommands,
            CancellationToken.None
        );

        await using var verify = await database.CreateDbContextAsync();
        var announcement = await verify.CustomAnnouncements.SingleAsync();
        announcement.LastOccurrenceAtUtc.ShouldBe(_enabledAt.UtcDateTime);
        announcement.LastSentAtUtc.ShouldBeNull();
    }

    [Test]
    public async Task ImportReEnable_DropsMissedIntervalAndWeeklyThenAllowsLaterRecurrences()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_disabledAt);
        var hostId = await SeedHostAsync(
            database,
            "streamer",
            changedAtUtc: _disabledAt.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
            database,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 120 },
            ["Interval"],
            _disabledAt.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
            database,
            hostId,
            new WeeklyCustomAnnouncementSchedule
            {
                Day = DayOfWeek.Monday,
                Time = new TimeOnly(9, 0),
            },
            ["Weekly"],
            _disabledAt.AddDays(-7).UtcDateTime
        );
        _ = await FeatureService(database, clock)
            .DisableAsync(hostId, HostFeatureFlags.CustomCommands, CancellationToken.None);
        clock.SetUtcNow(_enabledAt);

        var outcome = await Coordinator(database, clock)
            .ApplyAsync(
                Session(hostId),
                EnablementDocument(),
                EnablementSelection(hostId),
                new("actor-id", "streamer"),
                CancellationToken.None
            );

        outcome
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>()
            .Result.ChangedSections.ShouldBe([ConfigurationSectionId.ChannelToolEnablement]);
        await using (var verify = await database.CreateDbContextAsync())
        {
            var announcements = await verify.CustomAnnouncements.ToArrayAsync();
            announcements.ShouldAllBe(x => x.LastOccurrenceAtUtc == _enabledAt.UtcDateTime);
            announcements.ShouldAllBe(x => x.LastSentAtUtc == null);
            (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
        }

        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(database, clock, sender);
        await scheduler.RunTickAsync(CancellationToken.None);
        sender.Messages.ShouldBeEmpty();

        clock.SetUtcNow(_enabledAt.AddHours(2));
        await scheduler.RunTickAsync(CancellationToken.None);
        sender.Messages.ShouldBe([new SentChatMessage("streamer", "Interval")]);

        clock.SetUtcNow(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        await scheduler.RunTickAsync(CancellationToken.None);
        sender.Messages.ShouldBe([
            new SentChatMessage("streamer", "Interval"),
            new SentChatMessage("streamer", "Weekly"),
        ]);
    }

    [Test]
    public async Task FailedImportReEnable_RollsBackOccurrenceBoundaryWithEnablementAndAudit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(
            new FailImportAuditSaveInterceptor()
        );
        var clock = new ManualTimeProvider(_disabledAt);
        var hostId = await SeedHostAsync(
            database,
            "streamer",
            changedAtUtc: _disabledAt.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
            database,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 120 },
            ["Interval"],
            _disabledAt.AddHours(-1).UtcDateTime
        );
        _ = await FeatureService(database, clock)
            .DisableAsync(hostId, HostFeatureFlags.CustomCommands, CancellationToken.None);
        clock.SetUtcNow(_enabledAt);

        var outcome = await Coordinator(database, clock)
            .ApplyAsync(
                Session(hostId),
                EnablementDocument(),
                EnablementSelection(hostId),
                new("actor-id", "streamer"),
                CancellationToken.None
            );

        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Failed>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomAnnouncements.SingleAsync()).LastOccurrenceAtUtc.ShouldBeNull();
        (await verify.Hosts.SingleAsync())
            .EnabledFeatures.Contains(HostFeatureFlags.CustomCommands)
            .ShouldBeFalse();
        (await verify.ConfigurationActivations.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    private static HostFeatureService FeatureService(
        SqliteBlokeBotDbFactory database,
        TimeProvider clock
    ) =>
        TestHostFeatureServices.Create(
            database,
            new(TestEventBus.Create<AppEventKind>()),
            [],
            clock
        );

    private static ConfigurationTransferCoordinator Coordinator(
        SqliteBlokeBotDbFactory database,
        TimeProvider clock
    ) =>
        new(
            database,
            new(new CustomCommandConfigurationGraphWriter(database, null!, clock), new(), clock),
            new GrantedAuthority(),
            new(),
            clock,
            NullLogger<ConfigurationTransferCoordinator>.Instance
        );

    private static ConfigurationDocumentV1 EnablementDocument() =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            _enabledAt,
            new("source", "0.12.0"),
            new(
                ChannelToolEnablement: ChannelToolEnablementMapper.FromFlags(
                    HostFeatureFlags.CustomCommands
                )
            )
        );

    private static ConfigurationImportSelection EnablementSelection(int hostId) =>
        new(
            hostId,
            [new(ConfigurationSectionId.ChannelToolEnablement, ImportConflictStrategy.Merge, [])],
            new HashSet<HostFeatureFlags> { HostFeatureFlags.CustomCommands }
        );

    private static AuthenticatedSession Session(int hostId)
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", AuthRole.Streamer);
        return new()
        {
            IsAuthenticated = true,
            UserId = "streamer-id",
            Login = "streamer",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private sealed class GrantedAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
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
