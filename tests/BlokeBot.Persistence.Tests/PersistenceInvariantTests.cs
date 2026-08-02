using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class PersistenceInvariantTests
{
    [Test]
    public async Task TwoActiveGiveawaysForHost_Saving_ThrowsDatabaseError()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);

        db.PointsGiveaways.AddRange(
            Giveaway(hostId, PointsGiveawayStatus.Active),
            Giveaway(hostId, PointsGiveawayStatus.Active)
        );

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task TwoUnresolvedRoundsForHost_Saving_ThrowsDatabaseError()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);
        var profileId = await SeedProfileAsync(db, hostId);

        db.Rounds.AddRange(
            Round(hostId, profileId, GuessRoundStatus.Open),
            Round(hostId, profileId, GuessRoundStatus.Closed)
        );

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task TwoDefaultProfilesForHost_Saving_ThrowsDatabaseError()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);
        await SeedProfileAsync(db, hostId);

        db.Profiles.Add(
            new GuessRoundProfile
            {
                HostId = hostId,
                Name = "Other",
                Slug = "other",
                IsDefault = true,
            }
        );

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task InvalidStatusOrKindToken_Inserting_ThrowsDatabaseError()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);

        await Should.ThrowAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO points_giveaways
                    (HostId, Status, StartedAtUtc, EndsAtUtc, MinimumPayout, MaximumPayout, WinnerCount, Eligibility)
                VALUES
                    ({hostId}, 'Bogus', {DateTime.UtcNow}, {DateTime.UtcNow.AddMinutes(
                    5
                )}, '10', '100', 1, 'everyone')
                """
            )
        );

        var command = Command(hostId, "command");
        db.CustomCommands.Add(command);
        await db.SaveChangesAsync();
        await Should.ThrowAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE custom_commands SET InvocationLimit = 'Bogus' WHERE Id = {command.Id}"
            )
        );

        await Should.ThrowAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_command_invocation_claims
                    (HostId, CustomCommandId, TwitchUserId, TwitchStreamId, ClaimedAtUtc)
                VALUES
                    ({hostId}, {command.Id}, NULL, NULL, {DateTime.UtcNow})
                """
            )
        );

        await Should.ThrowAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO command_aliases (HostId, Kind, Alias)
                VALUES ({hostId}, 'Bogus', 'bogus')
                """
            )
        );
    }

    [Test]
    public async Task InvalidCustomCommandOrAlertToken_Inserting_ThrowsDatabaseError()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);

        await Should.ThrowAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_message_library_entries
                    (HostId, Name, SelectionMode, CurrentVariantIndex, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({hostId}, 'message', 'Bogus', 0, {DateTime.UtcNow}, {DateTime.UtcNow})
                """
            )
        );

        await Should.ThrowAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO durable_alerts
                    (HostId, Severity, Source, SourceKey, Title, Message, CreatedAtUtc)
                VALUES
                    ({hostId}, 'Bogus', 'test', 'one', 'Title', 'Message', {DateTime.UtcNow})
                """
            )
        );
    }

    [Test]
    public async Task InconsistentActionOrSchedulePayload_Inserting_ThrowsDatabaseError()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);
        var entry = MessageEntry(hostId, "message");
        var counter = Counter(hostId, "counter");
        var messageCommand = Command(hostId, "message-command");
        var counterCommand = Command(hostId, "counter-command");
        var announcement = Announcement(hostId, "announcement", entry);
        db.AddRange(entry, counter, messageCommand, counterCommand, announcement);
        await db.SaveChangesAsync();

        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_command_actions
                    (CustomCommandId, HostId, ZeroArgumentMessageLibraryEntryId, ActionType, CounterId)
                VALUES
                    ({messageCommand.Id}, {hostId}, {entry.Id}, 'Message', {counter.Id})
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_command_actions
                    (CustomCommandId, HostId, ZeroArgumentMessageLibraryEntryId, ActionType, CounterId)
                VALUES
                    ({counterCommand.Id}, {hostId}, {entry.Id}, 'Counter', NULL)
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcement_schedules
                    (CustomAnnouncementId, HostId, ScheduleType, IntervalMinutes,
                     RequiredChatMessages, WeeklyDay, WeeklyTime)
                VALUES
                    ({announcement.Id}, {hostId}, 'Weekly', 30, NULL, 5, '19:30:00')
                """
            )
        );
    }

    [Test]
    public async Task CrossHostCustomCommandReference_Inserting_ThrowsDatabaseError()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var firstHostId = await SeedHostAsync(db);
        var secondHostId = await SeedHostAsync(db);
        var firstEntry = MessageEntry(firstHostId, "first-message");
        var secondEntry = MessageEntry(secondHostId, "second-message");
        var secondCounter = Counter(secondHostId, "second-counter");
        var firstCommand = Command(firstHostId, "first-command");
        var secondCommand = Command(firstHostId, "second-command");
        var firstAnnouncement = Announcement(firstHostId, "announcement", firstEntry);
        var secondProfile = new GuessRoundProfile
        {
            HostId = secondHostId,
            Name = "Second profile",
            Slug = "second-profile",
        };
        db.AddRange(
            firstEntry,
            secondEntry,
            secondCounter,
            firstCommand,
            secondCommand,
            firstAnnouncement,
            secondProfile
        );
        await db.SaveChangesAsync();
        var wrongMessagePolicy = DeliveryPolicy(firstHostId);
        db.Add(wrongMessagePolicy);
        await db.SaveChangesAsync();

        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_command_actions
                    (CustomCommandId, HostId, ZeroArgumentMessageLibraryEntryId, ActionType, CounterId)
                VALUES
                    ({firstCommand.Id}, {firstHostId}, {secondEntry.Id}, 'Message', NULL)
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_command_actions
                    (CustomCommandId, HostId, ZeroArgumentMessageLibraryEntryId, ActionType, CounterId)
                VALUES
                    ({secondCommand.Id}, {firstHostId}, {firstEntry.Id}, 'Counter', {secondCounter.Id})
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_command_aliases (HostId, CustomCommandId, Alias)
                VALUES ({secondHostId}, {firstCommand.Id}, 'wrong-host')
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcements
                    (HostId, Name, Enabled, MessageLibraryEntryId, DeliveryPolicyId, LastSentAtUtc,
                     ChatMessagesSinceLastSent, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({firstHostId}, 'wrong-message', 1, {secondEntry.Id},
                     {wrongMessagePolicy.Id}, NULL, 0,
                     {DateTime.UtcNow}, {DateTime.UtcNow})
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcement_schedules
                    (CustomAnnouncementId, HostId, ScheduleType, IntervalMinutes,
                     RequiredChatMessages, WeeklyDay, WeeklyTime)
                VALUES
                    ({firstAnnouncement.Id}, {secondHostId}, 'Interval', 30, NULL, NULL, NULL)
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO command_aliases (HostId, GuessRoundProfileId, Kind, Alias)
                VALUES ({firstHostId}, {secondProfile.Id}, 'Guess', 'wrong-profile')
                """
            )
        );
    }

    [Test]
    public async Task OwnedCommandAndAnnouncementGraph_Deleting_CascadesVariantsButPreservesSharedMessages()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);
        var entry = MessageEntry(hostId, "message");
        db.CustomMessageLibraryEntries.Add(entry);
        await db.SaveChangesAsync();
        var command = Command(hostId, "command");
        command.Action = new MessageCustomCommandAction
        {
            HostId = hostId,
            ZeroArgumentMessageLibraryEntryId = entry.Id,
        };
        command.Aliases.Add(new CustomCommandAlias { HostId = hostId, Alias = "command" });
        var announcement = Announcement(hostId, "announcement", entry);
        announcement.Schedule = new WeeklyCustomAnnouncementSchedule
        {
            HostId = hostId,
            Day = DayOfWeek.Friday,
            Time = new TimeOnly(19, 30),
        };
        db.AddRange(command, announcement);
        await db.SaveChangesAsync();

        var deliveryPolicy = announcement.DeliveryPolicy;
        db.RemoveRange(command, announcement);
        await db.SaveChangesAsync();
        db.Remove(deliveryPolicy);
        await db.SaveChangesAsync();

        (await db.CustomCommandActions.CountAsync()).ShouldBe(0);
        (await db.CustomCommandAliases.CountAsync()).ShouldBe(0);
        (await db.CustomAnnouncementSchedules.CountAsync()).ShouldBe(0);
        (await db.CustomAnnouncementDeliveryPolicies.CountAsync()).ShouldBe(0);
        (await db.CustomMessageLibraryEntries.CountAsync()).ShouldBe(1);
    }

    [Test]
    public void PersistedEnums_FormattingAndParsing_UseExactRoundTrippableTokens()
    {
        AssertTokens<AccessListEntryKind>([
            (AccessListEntryKind.Blacklist, "blacklist"),
            (AccessListEntryKind.Whitelist, "whitelist"),
        ]);
        AssertTokens<AnnouncementOccurrenceStatus>([
            (AnnouncementOccurrenceStatus.Accepted, "Accepted"),
            (AnnouncementOccurrenceStatus.Attempting, "Attempting"),
            (AnnouncementOccurrenceStatus.None, "None"),
            (AnnouncementOccurrenceStatus.Pending, "Pending"),
            (AnnouncementOccurrenceStatus.RetryScheduled, "RetryScheduled"),
            (AnnouncementOccurrenceStatus.SkippedExpired, "SkippedExpired"),
            (AnnouncementOccurrenceStatus.TerminalAmbiguous, "TerminalAmbiguous"),
            (AnnouncementOccurrenceStatus.TerminalInvalidTimeZone, "TerminalInvalidTimeZone"),
            (AnnouncementOccurrenceStatus.TerminalMissingMessage, "TerminalMissingMessage"),
            (AnnouncementOccurrenceStatus.TerminalRejected, "TerminalRejected"),
            (AnnouncementOccurrenceStatus.TerminalUnexpected, "TerminalUnexpected"),
        ]);
        AssertTokens<AppCommandKind>([
            (AppCommandKind.AddPoints, "AddPoints"),
            (AppCommandKind.CancelGiveaway, "CancelGiveaway"),
            (AppCommandKind.Commands, "Commands"),
            (AppCommandKind.EndGiveaway, "EndGiveaway"),
            (AppCommandKind.Gamble, "Gamble"),
            (AppCommandKind.Giveaway, "Giveaway"),
            (AppCommandKind.GivePoints, "GivePoints"),
            (AppCommandKind.Guess, "Guess"),
            (AppCommandKind.Guesses, "Guesses"),
            (AppCommandKind.Join, "Join"),
            (AppCommandKind.Points, "Points"),
            (AppCommandKind.RemovePoints, "RemovePoints"),
            (AppCommandKind.Start, "Start"),
            (AppCommandKind.Stop, "Stop"),
            (AppCommandKind.Win, "Win"),
        ]);
        AssertTokens<CustomCommandCooldownScope>([
            (CustomCommandCooldownScope.Global, "Global"),
            (CustomCommandCooldownScope.User, "User"),
        ]);
        AssertTokens<CustomCommandInvocationLimit>([
            (CustomCommandInvocationLimit.OncePerStream, "OncePerStream"),
            (CustomCommandInvocationLimit.OncePerStreamPerUser, "OncePerStreamPerUser"),
            (CustomCommandInvocationLimit.OncePerUser, "OncePerUser"),
            (CustomCommandInvocationLimit.Unlimited, "Unlimited"),
        ]);
        AssertTokens<CustomCommandInvocationResetScope>([
            (CustomCommandInvocationResetScope.AllViewers, "AllViewers"),
            (CustomCommandInvocationResetScope.OneViewer, "OneViewer"),
        ]);
        AssertTokens<CustomMessageSelectionMode>([
            (CustomMessageSelectionMode.First, "First"),
            (CustomMessageSelectionMode.Random, "Random"),
            (CustomMessageSelectionMode.Sequential, "Sequential"),
        ]);
        AssertTokens<DurableAlertSeverity>([
            (DurableAlertSeverity.Critical, "Critical"),
            (DurableAlertSeverity.Info, "Info"),
            (DurableAlertSeverity.Warning, "Warning"),
        ]);
        AssertTokens<GuessRoundStatus>([
            (GuessRoundStatus.Closed, "Closed"),
            (GuessRoundStatus.Completed, "Completed"),
            (GuessRoundStatus.Open, "Open"),
        ]);
        AssertTokens<PointsEligibilityMode>([
            (PointsEligibilityMode.Everyone, "everyone"),
            (PointsEligibilityMode.Followers, "followers"),
            (PointsEligibilityMode.Subscribers, "subscribers"),
        ]);
        AssertTokens<PointsGiveawayStatus>([
            (PointsGiveawayStatus.Active, "Active"),
            (PointsGiveawayStatus.Cancelled, "Cancelled"),
            (PointsGiveawayStatus.Completed, "Completed"),
            (PointsGiveawayStatus.Expired, "Expired"),
        ]);
    }

    private static PointsGiveaway Giveaway(int hostId, PointsGiveawayStatus status) =>
        new()
        {
            HostId = hostId,
            Status = status,
            StartedAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddMinutes(5),
        };

    private static GuessRound Round(int hostId, int profileId, GuessRoundStatus status) =>
        new()
        {
            HostId = hostId,
            GuessRoundProfileId = profileId,
            Status = status,
            StartedAtUtc = DateTime.UtcNow,
            ClosedAtUtc = status == GuessRoundStatus.Open ? null : DateTime.UtcNow,
        };

    private static CustomMessageLibraryEntry MessageEntry(int hostId, string name) =>
        new()
        {
            HostId = hostId,
            Name = name,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static CustomCounter Counter(int hostId, string name) =>
        new()
        {
            HostId = hostId,
            Name = name,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static CustomCommand Command(int hostId, string name) =>
        new()
        {
            HostId = hostId,
            Name = name,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static CustomAnnouncement Announcement(
        int hostId,
        string name,
        CustomMessageLibraryEntry entry
    ) =>
        new()
        {
            HostId = hostId,
            Name = name,
            MessageLibraryEntry = entry,
            DeliveryPolicy = DeliveryPolicy(hostId),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy DeliveryPolicy(
        int hostId
    ) =>
        new()
        {
            HostId = hostId,
            RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(2)),
            OccurrenceLifetime = new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(30)),
        };

    private static void AssertTokens<TEnum>(IReadOnlyList<(TEnum Value, string Token)> cases)
        where TEnum : struct, Enum
    {
        PersistedEnumTokens<TEnum>.Values.ShouldBe(cases.Select(item => item.Token));
        foreach (var (value, token) in cases)
        {
            PersistedEnumTokens<TEnum>.Format(value).ShouldBe(token);
            PersistedEnumTokens<TEnum>.Parse(token).ShouldBe(value);
        }
    }

    private static async Task<int> SeedHostAsync(BlokeBotDbContext db)
    {
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = $"host-{Guid.NewGuid():N}",
            DisplayName = "Host",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<int> SeedProfileAsync(BlokeBotDbContext db, int hostId)
    {
        var profile = new GuessRoundProfile
        {
            HostId = hostId,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }
}
