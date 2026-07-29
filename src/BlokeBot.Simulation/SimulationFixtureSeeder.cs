using BlokeBot.Announcements;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Simulation;

internal sealed class SimulationFixtureSeeder(
    BotHostProvisioningService provisioning,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider timeProvider
)
{
    public async Task<BotHostChoice> SeedAsync(CancellationToken cancellationToken)
    {
        var hostId = await provisioning.EnsureHostAsync(
            SimulationMode.Login,
            SimulationMode.UserId,
            SimulationMode.DisplayName,
            null,
            cancellationToken
        );
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleAsync(x => x.Id == hostId, cancellationToken);
        host.DisplayName = SimulationMode.DisplayName;
        host.EnabledFeatures = HostFeatureFlags.All;
        host.TimeZoneId = "UTC";

        await SeedGuessingAsync(db, hostId, now, cancellationToken);
        await SeedPointsAsync(db, hostId, now, cancellationToken);
        await SeedCustomCommandsAsync(db, hostId, now, cancellationToken);
        await SeedAlertsAsync(db, hostId, now, cancellationToken);
        await SeedAutomaticRaidShoutoutsAsync(db, hostId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new BotHostChoice(
            hostId,
            SimulationMode.Login,
            SimulationMode.DisplayName,
            AuthRole.Streamer
        );
    }

    private static async Task SeedGuessingAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var profile = await db.Profiles.SingleAsync(
            x => x.HostId == hostId && x.IsDefault,
            cancellationToken
        );
        if (
            !await db.GuessOptions.AnyAsync(
                x => x.GuessRoundProfileId == profile.Id,
                cancellationToken
            )
        )
        {
            profile.WinningGuessPointReward = "250";
            profile.Options.AddRange([
                new GuessOption
                {
                    Name = "Blue",
                    ReplyText = "@{user} picked Blue.",
                    SortOrder = 0,
                },
                new GuessOption
                {
                    Name = "Red",
                    ReplyText = "@{user} picked Red.",
                    SortOrder = 1,
                },
                new GuessOption
                {
                    Name = "Gold",
                    ReplyText = "@{user} picked Gold.",
                    SortOrder = 2,
                },
            ]);
        }

        if (await db.Rounds.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        db.Rounds.AddRange(
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Completed,
                StartedAtUtc = now.AddHours(-3),
                ClosedAtUtc = now.AddHours(-2).AddMinutes(-45),
                WinningName = "Blue",
                Votes =
                [
                    Vote("nightowl", "Blue", now.AddHours(-2).AddMinutes(-58)),
                    Vote("chatregular", "Red", now.AddHours(-2).AddMinutes(-56)),
                    Vote("newviewer", "Blue", now.AddHours(-2).AddMinutes(-54)),
                ],
            },
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Completed,
                StartedAtUtc = now.AddHours(-2),
                ClosedAtUtc = now.AddHours(-1).AddMinutes(-45),
                WinningName = "Red",
                Votes =
                [
                    Vote("nightowl", "Gold", now.AddHours(-1).AddMinutes(-58)),
                    Vote("chatregular", "Red", now.AddHours(-1).AddMinutes(-56)),
                    Vote("newviewer", "Red", now.AddHours(-1).AddMinutes(-52)),
                ],
            },
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = now.AddMinutes(-8),
                Votes =
                [
                    Vote("nightowl", "Blue", now.AddMinutes(-7)),
                    Vote("chatregular", "Gold", now.AddMinutes(-6)),
                    Vote("newviewer", "Blue", now.AddMinutes(-5)),
                    Vote("firsttimer", "Red", now.AddMinutes(-3)),
                ],
            }
        );
    }

    private static GuessVote Vote(string login, string guess, DateTime guessedAtUtc)
    {
        return new GuessVote
        {
            Login = login,
            GuessName = guess,
            GuessedAtUtc = guessedAtUtc,
        };
    }

    private static async Task SeedPointsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (!await db.PointBalances.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            var balances = new[]
            {
                Balance(hostId, "nightowl", "1840", now.AddMinutes(-12)),
                Balance(hostId, "chatregular", "1325", now.AddMinutes(-18)),
                Balance(hostId, "newviewer", "910", now.AddMinutes(-25)),
                Balance(hostId, "firsttimer", "250", now.AddMinutes(-32)),
            };
            db.PointBalances.AddRange(balances);
            db.PointLedgerEntries.AddRange(
                balances.Select(
                    (balance, index) =>
                        new PointLedgerEntry
                        {
                            HostId = hostId,
                            Kind = PointLedgerKind.Add,
                            Login = balance.Login,
                            Delta = balance.Amount,
                            BalanceAfter = balance.Amount,
                            ActorLogin = SimulationMode.Login,
                            Note = "Stream reward",
                            CreatedAtUtc = now.AddMinutes(-32 + index * 6),
                        }
                )
            );
        }

        if (await db.PointsGiveaways.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = hostId,
                Status = PointsGiveawayStatus.Active,
                StartedAtUtc = now.AddMinutes(-10),
                EndsAtUtc = now.AddMinutes(20),
                MinimumPayout = "100",
                MaximumPayout = "500",
                WinnerCount = 2,
                Eligibility = PointsEligibilityMode.Everyone,
                Entrants =
                [
                    new PointsGiveawayEntrant
                    {
                        Login = "nightowl",
                        JoinedAtUtc = now.AddMinutes(-9),
                    },
                    new PointsGiveawayEntrant
                    {
                        Login = "chatregular",
                        JoinedAtUtc = now.AddMinutes(-8),
                    },
                    new PointsGiveawayEntrant
                    {
                        Login = "newviewer",
                        JoinedAtUtc = now.AddMinutes(-7),
                    },
                ],
            }
        );
    }

    private static PointBalance Balance(
        int hostId,
        string login,
        string amount,
        DateTime updatedAtUtc
    )
    {
        return new PointBalance
        {
            HostId = hostId,
            Login = login,
            Amount = amount,
            UpdatedAtUtc = updatedAtUtc,
        };
    }

    private static async Task SeedCustomCommandsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.CustomCommands.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        var welcome = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = "Welcome",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Variants =
            [
                new CustomMessageVariant
                {
                    SortOrder = 0,
                    Text = "Welcome in, @{user}! Make yourself at home.",
                },
                new CustomMessageVariant { SortOrder = 1, Text = "Good to see you, @{user}." },
            ],
        };
        var hydrationReply = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = "Hydration reminder",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Variants =
            [
                new CustomMessageVariant
                {
                    SortOrder = 0,
                    Text = "Hydration reminder number {count}: take a sip of water.",
                },
            ],
        };
        var counter = new CustomCounter
        {
            HostId = hostId,
            Name = "Hydration reminders",
            Value = 12,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.AddRange(welcome, hydrationReply, counter);
        await db.SaveChangesAsync(cancellationToken);

        db.CustomCommands.AddRange(
            new CustomCommand
            {
                HostId = hostId,
                Name = "Welcome viewer",
                CooldownSeconds = 10,
                Action = new MessageCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = welcome.Id,
                },
                Aliases = [new CustomCommandAlias { HostId = hostId, Alias = "welcome" }],
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new CustomCommand
            {
                HostId = hostId,
                Name = "Hydration counter",
                CooldownSeconds = 30,
                Action = new CounterCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = hydrationReply.Id,
                    CounterId = counter.Id,
                },
                Aliases = [new CustomCommandAlias { HostId = hostId, Alias = "hydrate" }],
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );

        db.CustomAnnouncements.Add(
            new CustomAnnouncement
            {
                HostId = hostId,
                Name = "Welcome reminder",
                Enabled = false,
                MessageLibraryEntryId = welcome.Id,
                DeliveryPolicy = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
                {
                    HostId = hostId,
                    RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(2)),
                    OccurrenceLifetime = new AnnouncementOccurrenceLifetime(
                        TimeSpan.FromSeconds(30)
                    ),
                },
                Schedule = new IntervalAfterChatCustomAnnouncementSchedule
                {
                    HostId = hostId,
                    IntervalMinutes = 30,
                    RequiredChatMessages = 5,
                },
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
    }

    private static async Task SeedAlertsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.DurableAlerts.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        db.DurableAlerts.AddRange(
            new DurableAlert
            {
                HostId = hostId,
                Severity = DurableAlertSeverity.Warning,
                Source = "twitch-outbound-queue",
                SourceKey = "simulation-pending-chat",
                Title = "Chat delivery is taking longer than usual",
                Message = "BlokeBot is keeping messages queued while chat recovers.",
                LinkPath = "/host",
                CreatedAtUtc = now.AddMinutes(-14),
            },
            new DurableAlert
            {
                HostId = hostId,
                Severity = DurableAlertSeverity.Info,
                Source = "simulation",
                SourceKey = "simulation-reconnected",
                Title = "Chat connection restored",
                Message = "Queued messages resumed after the connection recovered.",
                CreatedAtUtc = now.AddHours(-2),
                AcknowledgedAtUtc = now.AddHours(-1).AddMinutes(-45),
                AcknowledgedByLogin = SimulationMode.Login,
            }
        );
    }

    private static async Task SeedAutomaticRaidShoutoutsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (
            !await db.AutomaticRaidShoutoutSettings.AnyAsync(
                value => value.HostId == hostId,
                cancellationToken
            )
        )
        {
            db.AutomaticRaidShoutoutSettings.Add(
                new AutomaticRaidShoutoutSettings
                {
                    HostId = hostId,
                    Enabled = true,
                    MinimumViewerCount = 10,
                    Mechanism = AutomaticRaidShoutoutMechanism.Chat,
                    ChatPresentation = AutomaticRaidChatPresentation.Pinned,
                    MessageTemplate =
                        "Welcome {twitch_handle}! Last seen playing {last_game|something fun}: {channel_url}",
                    PinDurationSeconds = 300,
                    AnnouncementColor = PersistedAnnouncementColor.Purple,
                    UpdatedAtUtc = now,
                }
            );
        }

        if (
            await db.AutomaticRaidShoutoutOutcomes.AnyAsync(
                value => value.HostId == hostId,
                cancellationToken
            )
        )
        {
            return;
        }

        db.AutomaticRaidShoutoutOutcomes.AddRange(
            Outcome(
                hostId,
                "simulation-raid-partial",
                "pinpal",
                "Pin Pal",
                84,
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                AutomaticRaidShoutoutResultCode.PartialFailure,
                now.AddMinutes(-8)
            ),
            Outcome(
                hostId,
                "simulation-raid-delivered",
                "cozystreamer",
                "Cozy Streamer",
                42,
                AutomaticRaidShoutoutOutcomeStatus.Delivered,
                AutomaticRaidShoutoutResultCode.Delivered,
                now.AddMinutes(-24)
            ),
            Outcome(
                hostId,
                "simulation-raid-authority",
                "newfriend",
                "New Friend",
                21,
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                AutomaticRaidShoutoutResultCode.AuthorityRequired,
                now.AddMinutes(-41)
            ),
            Outcome(
                hostId,
                "simulation-raid-cooldown",
                "speedrunner",
                "Speed Runner",
                16,
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                AutomaticRaidShoutoutResultCode.Cooldown,
                now.AddMinutes(-58)
            )
        );
    }

    private static AutomaticRaidShoutoutOutcome Outcome(
        int hostId,
        string providerMessageId,
        string sourceLogin,
        string sourceDisplayName,
        int viewerCount,
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode resultCode,
        DateTime timestamp
    )
    {
        return new AutomaticRaidShoutoutOutcome
        {
            HostId = hostId,
            ProviderMessageId = providerMessageId,
            SourceTwitchUserId = $"{sourceLogin}-id",
            SourceLogin = sourceLogin,
            SourceDisplayName = sourceDisplayName,
            ViewerCount = viewerCount,
            Status = status,
            ResultCode = resultCode,
            MessageTimestampUtc = timestamp,
            ClaimedAtUtc = timestamp,
            CompletedAtUtc = timestamp.AddSeconds(2),
        };
    }
}
