using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class ViewerPrivacyServiceTests
{
    private const string _aliceId = "1001";
    private const string _alice = "alice";
    private const string _bobId = "1002";
    private const string _bob = "bob";

    [Test]
    public async Task Erasure_AcrossEveryFeature_DeletesSafeRowsStripsRetainedRowsAndIsIdempotent()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var subject = PrivacySubject.Create(_aliceId, _alice);
            var report = await ViewerPrivacyService.EraseAsync(
                db,
                subject,
                hostId: null,
                CancellationToken.None
            );
            report.TotalChangedRows.ShouldBeGreaterThan(0);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.Votes.CountAsync(x => x.Login == _alice)).ShouldBe(0);
            (await db.Votes.CountAsync(x => x.Login == _bob)).ShouldBe(1);
            (await db.PointBalances.CountAsync(x => x.Login == _alice)).ShouldBe(0);
            (await db.PointBalances.CountAsync(x => x.Login == _bob)).ShouldBe(1);

            var aliceLedger = await db.PointLedgerEntries.SingleAsync(x =>
                x.Id == fixture.AliceLedgerEntryId
            );
            aliceLedger.Login.ShouldBe(ViewerPrivacyService.ErasedToken);
            aliceLedger.Note.ShouldBeEmpty();
            aliceLedger.Delta.ShouldBe("10");
            aliceLedger.BalanceAfter.ShouldBe("10");
            var bobLedger = await db.PointLedgerEntries.SingleAsync(x =>
                x.Id == fixture.BobLedgerEntryId
            );
            bobLedger.Login.ShouldBe(_bob);
            bobLedger.CounterpartyLogin.ShouldBeNull();
            bobLedger.Note.ShouldBe("gift from alice");

            (await db.PointsGiveawayEntrants.CountAsync(x => x.Login == _alice)).ShouldBe(0);
            (await db.PointsGiveawayEntrants.CountAsync(x => x.Login == _bob)).ShouldBe(1);
            var win = await db.PointsGiveawayWinners.SingleAsync();
            win.Login.ShouldBe(ViewerPrivacyService.ErasedToken);
            win.Payout.ShouldBe("25");

            (await db.CustomCommandAllowedUsers.CountAsync()).ShouldBe(1);
            (await db.CustomCommandAllowedUsers.SingleAsync()).Login.ShouldBe(_bob);
            (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
            (await db.CustomCommandInvocationClaims.SingleAsync()).TwitchUserId.ShouldBe(_bobId);
            var resetAudit = await db.CustomCommandInvocationResetAudits.SingleAsync();
            resetAudit.ActorTwitchUserId.ShouldBe(ViewerPrivacyService.ErasedToken);
            resetAudit.ActorLogin.ShouldBe(ViewerPrivacyService.ErasedToken);
            resetAudit.TargetTwitchUserId.ShouldBeNull();
            resetAudit.TargetLogin.ShouldBeNull();
            resetAudit.AffectedClaimCount.ShouldBe(3);

            (await db.DurableAlerts.SingleAsync()).AcknowledgedByLogin.ShouldBeNull();
            (await db.SiteAccessEntries.CountAsync(x => x.Login == _alice)).ShouldBe(0);
            (await db.SiteAccessEntries.CountAsync(x => x.Login == _bob)).ShouldBe(1);
            (await db.HostModAccessEntries.CountAsync(x => x.Login == _alice)).ShouldBe(0);
            (await db.WhisperQuotaRecipients.CountAsync()).ShouldBe(1);
            (await db.WhisperQuotaRecipients.SingleAsync()).RecipientLogin.ShouldBe(_bob);

            (
                await db.ShoutoutHistory.CountAsync(x =>
                    x.SourceLogin == _alice || x.TargetLogin == _alice
                )
            ).ShouldBe(0);
            (await db.ShoutoutHistory.CountAsync()).ShouldBe(1);
            (await db.ShoutoutCooldowns.CountAsync(x => x.TargetLogin == _alice)).ShouldBe(0);
            (await db.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(1);
            (await db.AutomaticRaidShoutoutOutcomes.SingleAsync()).SourceLogin.ShouldBe(_bob);

            (await db.TwitchRewardRedemptions.CountAsync(x => x.UserLogin == _alice)).ShouldBe(0);
            (await db.TwitchRewardRedemptions.CountAsync(x => x.UserLogin == _bob)).ShouldBe(1);
            var clip = await db.TwitchClips.SingleAsync();
            clip.CreatorTwitchUserId.ShouldBeNull();
            clip.CreatorLogin.ShouldBeNull();
            clip.BroadcasterLogin.ShouldBe("streamer-a");

            (await db.RequestSubmissions.CountAsync(x => x.SubmitterLogin == _alice)).ShouldBe(0);
            var bobSubmission = await db
                .RequestSubmissions.Include(x => x.Votes)
                .SingleAsync(x => x.SubmitterLogin == _bob);
            bobSubmission.Votes.ShouldBeEmpty();
            bobSubmission.VoteCount.ShouldBe(0);
            (await db.RequestSubmissionValues.CountAsync()).ShouldBe(0);
            (await db.RequestBoardEvents.CountAsync()).ShouldBe(1);
            (await db.RequestBoardEvents.SingleAsync()).PublicPayload.ShouldContain(_bob);

            (await db.PlayQueueEntries.CountAsync()).ShouldBe(1);
            (await db.PlayQueueEntries.SingleAsync()).NormalizedLogin.ShouldBe(_bob);
            (await db.PlayQueueEntryValues.CountAsync()).ShouldBe(0);
            (await db.PlayQueueParticipation.CountAsync()).ShouldBe(0);
            (await db.PlayQueueExclusions.CountAsync()).ShouldBe(0);
            (await db.PlayQueueEvents.CountAsync()).ShouldBe(1);

            (await db.MomentContributors.CountAsync()).ShouldBe(1);
            (await db.MomentContributors.SingleAsync()).NormalizedLogin.ShouldBe(_bob);
            (await db.MomentCaptureRequests.CountAsync()).ShouldBe(0);
            (await db.MomentSuggestions.CountAsync()).ShouldBe(0);
            (await db.MomentVotes.CountAsync()).ShouldBe(0);
            var audit = await db.MomentModerationAudit.SingleAsync();
            audit.ActorLogin.ShouldBe(ViewerPrivacyService.ErasedToken);
            audit.PrivateText.ShouldBeEmpty();
            var merge = await db.MomentMerges.SingleAsync();
            merge.ActorLogin.ShouldBe(ViewerPrivacyService.ErasedToken);
            merge.PrivateText.ShouldBeEmpty();
            (await db.MomentEvents.CountAsync()).ShouldBe(1);

            var overlayEvent = await db.OverlayInstanceEvents.SingleAsync();
            overlayEvent.ActorUserId.ShouldBe(ViewerPrivacyService.ErasedToken);
            overlayEvent.ActorLogin.ShouldBe(ViewerPrivacyService.ErasedToken);
            (await db.OverlayEventFeedItems.CountAsync()).ShouldBe(1);
            (await db.OverlayEventFeedItems.SingleAsync()).Body.ShouldNotContain(_alice);

            (await db.PublicChatPinOperations.SingleAsync()).PinnerTwitchUserId.ShouldBeNull();
            (await db.ActivePublicChatPins.SingleAsync()).PinnerTwitchUserId.ShouldBe(
                ViewerPrivacyService.ErasedToken
            );

            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
            (await db.AutomationFlowRuns.SingleAsync()).ContextJson.ShouldContain(_bob);
            (await db.AutomationNodeRuns.CountAsync()).ShouldBe(1);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var subject = PrivacySubject.Create(_aliceId, _alice);
            var rerun = await ViewerPrivacyService.EraseAsync(
                db,
                subject,
                hostId: null,
                CancellationToken.None
            );
            rerun.TotalChangedRows.ShouldBe(0);
        }
    }

    [Test]
    public async Task HostScopedErasure_LeavesTheSameViewersDataInOtherHostsUntouched()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var subject = PrivacySubject.Create(_aliceId, _alice);
            _ = await ViewerPrivacyService.EraseAsync(
                db,
                subject,
                fixture.HostAId,
                CancellationToken.None
            );
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            (
                await db.PointBalances.CountAsync(x =>
                    x.HostId == fixture.HostAId && x.Login == _alice
                )
            ).ShouldBe(0);
            (
                await db.PointBalances.CountAsync(x =>
                    x.HostId == fixture.HostBId && x.Login == _alice
                )
            ).ShouldBe(1);
            (
                await db.HostModAccessEntries.CountAsync(x =>
                    x.HostId == fixture.HostBId && x.Login == _alice
                )
            ).ShouldBe(1);
            (await db.SiteAccessEntries.CountAsync(x => x.Login == _alice)).ShouldBe(1);
        }
    }

    [Test]
    public async Task Export_LocatesSubjectRowsAcrossFeaturesWithoutOtherViewersRows()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedAsync(factory);

        await using var db = await factory.CreateDbContextAsync();
        var subject = PrivacySubject.Create(_aliceId, _alice);
        var export = await ViewerPrivacyService.ExportAsync(
            db,
            subject,
            hostId: null,
            CancellationToken.None
        );

        export.Sections.Keys.ShouldBe(
            [
                "guessing.votes",
                "points.balances",
                "points.ledger",
                "points.giveaway-entries",
                "points.giveaway-wins",
                "commands.allowed-users",
                "commands.usage-claims",
                "commands.reset-audits",
                "alerts.acknowledgements",
                "access.site-entries",
                "access.mod-entries",
                "whispers.recipients",
                "shoutouts.history",
                "shoutouts.cooldowns",
                "shoutouts.raid-outcomes",
                "channel-points.redemptions",
                "clips.created",
                "request-boards.submissions",
                "request-boards.votes",
                "play-queues.entries",
                "play-queues.participation",
                "play-queues.exclusions",
                "moments.contributors",
                "moments.capture-requests",
                "moments.suggestions",
                "moments.votes",
                "moments.moderation-audits",
                "moments.merges",
                "overlays.actor-events",
                "public-chat.pins",
            ],
            ignoreOrder: true
        );
        export.Sections["points.balances"].Count.ShouldBe(2);
        export.Sections["points.ledger"].Count.ShouldBe(2);
        export.Sections["guessing.votes"].Count.ShouldBe(1);
        export.Sections["request-boards.votes"].Count.ShouldBe(1);
    }

    [Test]
    public void EveryIdentityBearingColumn_IsDeclaredHandledOrDeliberatelyExcluded()
    {
        // Tripwire: a new persisted column that can carry a viewer's Twitch identity must be
        // wired into ViewerPrivacyService (and the published notice) or explicitly excluded here
        // with a reason before the suite passes again.
        var handled = new HashSet<string>(StringComparer.Ordinal)
        {
            "GuessVote.Login",
            "PointBalance.Login",
            "PointLedgerEntry.Login",
            "PointLedgerEntry.ActorLogin",
            "PointLedgerEntry.CounterpartyLogin",
            "PointsGiveawayEntrant.Login",
            "PointsGiveawayWinner.Login",
            "CustomCommandAllowedUser.TwitchUserId",
            "CustomCommandAllowedUser.Login",
            "CustomCommandAllowedUser.DisplayName",
            "CustomCommandInvocationClaim.TwitchUserId",
            "CustomCommandInvocationResetAudit.ActorTwitchUserId",
            "CustomCommandInvocationResetAudit.ActorLogin",
            "CustomCommandInvocationResetAudit.TargetTwitchUserId",
            "CustomCommandInvocationResetAudit.TargetLogin",
            "DurableAlert.AcknowledgedByLogin",
            "SiteAccessEntry.Login",
            "HostModAccessEntry.Login",
            "WhisperQuotaRecipient.RecipientTwitchUserId",
            "WhisperQuotaRecipient.RecipientLogin",
            "ShoutoutHistoryEntry.SourceTwitchUserId",
            "ShoutoutHistoryEntry.SourceLogin",
            "ShoutoutHistoryEntry.TargetTwitchUserId",
            "ShoutoutHistoryEntry.TargetLogin",
            "ShoutoutCooldownState.TargetTwitchUserId",
            "ShoutoutCooldownState.TargetLogin",
            "AutomaticRaidShoutoutOutcome.SourceTwitchUserId",
            "AutomaticRaidShoutoutOutcome.SourceLogin",
            "AutomaticRaidShoutoutOutcome.SourceDisplayName",
            "TwitchRewardRedemption.UserId",
            "TwitchRewardRedemption.UserLogin",
            "TwitchRewardRedemption.UserInput",
            "TwitchClip.CreatorTwitchUserId",
            "TwitchClip.CreatorLogin",
            "RequestSubmission.SubmitterLogin",
            "RequestSubmissionVote.VoterLogin",
            "BountyPledge.ContributorTwitchUserId",
            "BountyPledge.ContributorLogin",
            "BountyContributorReward.TwitchUserId",
            "BountyContributorReward.Login",
            "BountyModerationAudit.ActorTwitchUserId",
            "BountyModerationAudit.ActorLogin",
            "PlayQueueEntry.TwitchUserId",
            "PlayQueueEntry.NormalizedLogin",
            "PlayQueueEntry.DisplayName",
            "PlayQueueEntry.IdentityKey",
            "PlayQueueParticipation.IdentityKey",
            "PlayQueueExclusion.IdentityKey",
            "MomentContributor.TwitchUserId",
            "MomentContributor.NormalizedLogin",
            "MomentContributor.DisplayName",
            "MomentContributor.IdentityKey",
            "MomentCaptureRequest.IdentityKey",
            "MomentSuggestion.IdentityKey",
            "MomentVote.TwitchUserId",
            "MomentVote.NormalizedLogin",
            "MomentVote.IdentityKey",
            "MomentModerationAudit.ActorLogin",
            "MomentMerge.ActorLogin",
            "OverlayInstanceDomainEvent.ActorUserId",
            "OverlayInstanceDomainEvent.ActorLogin",
            "PublicChatPinOperation.PinnerTwitchUserId",
            "ActivePublicChatPin.PinnerTwitchUserId",
            "CommunityAudit.ActorLogin",
            "CommunityAudit.ActorTwitchUserId",
            "CommunityCompletion.ViewerDisplayName",
            "CommunityCompletion.ViewerLogin",
            "CommunityCompletion.ViewerTwitchUserId",
            "CommunityEquippedReward.ViewerLogin",
            "CommunityEquippedReward.ViewerTwitchUserId",
            "CommunityProgress.ViewerDisplayName",
            "CommunityProgress.ViewerLogin",
            "CommunityProgress.ViewerTwitchUserId",
            "CommunityRewardUnlock.ViewerDisplayName",
            "CommunityRewardUnlock.ViewerLogin",
            "CommunityRewardUnlock.ViewerTwitchUserId",
            "CommunitySeasonStanding.ViewerDisplayName",
            "CommunitySeasonStanding.ViewerLogin",
            "CommunitySeasonStanding.ViewerTwitchUserId",
        };
        var excluded = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BotHost.TwitchUserId"] = "Hosted channel identity; erased by channel removal.",
            ["BotHost.Login"] = "Hosted channel identity; erased by channel removal.",
            ["BotHost.DisplayName"] = "Hosted channel identity; erased by channel removal.",
            ["HostBotAccountSettings.TwitchUserId"] =
                "Channel bot service account; erased by disconnect or channel removal.",
            ["HostBotAccountSettings.Login"] =
                "Channel bot service account; erased by disconnect or channel removal.",
            ["HostBotAccountSettings.DisplayName"] =
                "Channel bot service account; erased by disconnect or channel removal.",
            ["HostBroadcasterAuthorization.TwitchUserId"] =
                "Broadcaster grant for the channel; erased by disconnect or channel removal.",
            ["HostBroadcasterAuthorization.Login"] =
                "Broadcaster grant for the channel; erased by disconnect or channel removal.",
            ["WhisperQuotaBucket.BotTwitchUserId"] =
                "Bot service account identity, not viewer data.",
            ["TwitchClip.BroadcasterTwitchUserId"] =
                "Hosted channel identity; erased by channel removal.",
            ["TwitchClip.BroadcasterLogin"] = "Hosted channel identity; erased by channel removal.",
        };

        var model = DesignModel();
        var identityColumns = model
            .GetEntityTypes()
            .Where(entity => entity.ClrType.Namespace == typeof(BotHost).Namespace)
            .SelectMany(entity =>
                entity
                    .GetDeclaredProperties()
                    .Where(property =>
                        property.ClrType == typeof(string) && IsIdentityName(property.Name)
                    )
                    .Select(property => $"{entity.ClrType.Name}.{property.Name}")
            )
            .ToHashSet(StringComparer.Ordinal);

        identityColumns.ShouldNotBeEmpty();
        var undeclared = identityColumns.Except(handled).Except(excluded.Keys).Order().ToArray();
        undeclared.ShouldBeEmpty(
            "Wire these columns into ViewerPrivacyService or exclude them with a reason"
        );
        var stale = handled.Concat(excluded.Keys).Except(identityColumns).Order().ToArray();
        stale.ShouldBeEmpty("These declarations no longer match a persisted column");
    }

    private static bool IsIdentityName(string name) =>
        name.Contains("Login", StringComparison.Ordinal)
        || name.Contains("TwitchUserId", StringComparison.Ordinal)
        || name.Contains("DisplayName", StringComparison.Ordinal)
        || name.Contains("IdentityKey", StringComparison.Ordinal)
        || name.Contains("ActorUserId", StringComparison.Ordinal)
        || name is "UserId" or "UserLogin" or "UserInput";

    private static Microsoft.EntityFrameworkCore.Metadata.IModel DesignModel()
    {
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new BlokeBotDbContext(options);
        return db.Model;
    }

    private sealed record Fixture(
        int HostAId,
        int HostBId,
        int AliceLedgerEntryId,
        int BobLedgerEntryId
    );

    private static async Task<Fixture> SeedAsync(SqliteBlokeBotDbFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var hostA = new BotHost
        {
            Login = "streamer-a",
            DisplayName = "Streamer A",
            TwitchUserId = "9001",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = now,
        };
        var hostB = new BotHost
        {
            Login = "streamer-b",
            DisplayName = "Streamer B",
            TwitchUserId = "9002",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = now,
        };
        db.Hosts.AddRange(hostA, hostB);
        _ = await db.SaveChangesAsync();
        var a = hostA.Id;
        var b = hostB.Id;

        var profile = new GuessRoundProfile
        {
            HostId = a,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
            ReplySettings = new BotReplySettings { AvailableGuessesReply = "Guesses: {options}" },
            Options = [new GuessOption { Name = "red", ReplyText = "Red" }],
        };
        _ = db.Profiles.Add(profile);
        _ = await db.SaveChangesAsync();
        var round = new GuessRound
        {
            HostId = a,
            GuessRoundProfileId = profile.Id,
            Status = GuessRoundStatus.Open,
            StartedAtUtc = now,
        };
        _ = db.Rounds.Add(round);
        _ = await db.SaveChangesAsync();
        db.Votes.AddRange(
            new GuessVote
            {
                GuessRoundId = round.Id,
                Login = _alice,
                GuessName = "red",
                GuessedAtUtc = now,
            },
            new GuessVote
            {
                GuessRoundId = round.Id,
                Login = _bob,
                GuessName = "red",
                GuessedAtUtc = now,
            }
        );

        db.PointBalances.AddRange(
            new PointBalance
            {
                HostId = a,
                Login = _alice,
                Amount = "10",
                UpdatedAtUtc = now,
            },
            new PointBalance
            {
                HostId = b,
                Login = _alice,
                Amount = "5",
                UpdatedAtUtc = now,
            },
            new PointBalance
            {
                HostId = a,
                Login = _bob,
                Amount = "20",
                UpdatedAtUtc = now,
            }
        );
        var aliceLedger = new PointLedgerEntry
        {
            HostId = a,
            CreatedAtUtc = now,
            Kind = PointLedgerKind.Add,
            Login = _alice,
            Delta = "10",
            BalanceAfter = "10",
            Note = "welcome grant",
        };
        var bobLedger = new PointLedgerEntry
        {
            HostId = a,
            CreatedAtUtc = now,
            Kind = PointLedgerKind.Add,
            Login = _bob,
            CounterpartyLogin = _alice,
            Delta = "5",
            BalanceAfter = "25",
            Note = "gift from alice",
        };
        db.PointLedgerEntries.AddRange(aliceLedger, bobLedger);

        var giveaway = new PointsGiveaway
        {
            HostId = a,
            Status = PointsGiveawayStatus.Completed,
            StartedAtUtc = now.AddMinutes(-10),
            EndsAtUtc = now.AddMinutes(-5),
            CompletedAtUtc = now,
            Entrants =
            [
                new PointsGiveawayEntrant { Login = _alice, JoinedAtUtc = now },
                new PointsGiveawayEntrant { Login = _bob, JoinedAtUtc = now },
            ],
            Winners = [new PointsGiveawayWinner { Login = _alice, Payout = "25" }],
        };
        _ = db.PointsGiveaways.Add(giveaway);

        var command = new CustomCommand
        {
            HostId = a,
            Name = "hype",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Action = new MessageCustomCommandAction { HostId = a },
        };
        _ = db.CustomCommands.Add(command);
        _ = await db.SaveChangesAsync();
        db.CustomCommandAllowedUsers.AddRange(
            new CustomCommandAllowedUser
            {
                HostId = a,
                CustomCommandId = command.Id,
                TwitchUserId = _aliceId,
                Login = _alice,
                DisplayName = "Alice",
            },
            new CustomCommandAllowedUser
            {
                HostId = a,
                CustomCommandId = command.Id,
                TwitchUserId = _bobId,
                Login = _bob,
                DisplayName = "Bob",
            }
        );
        db.CustomCommandInvocationClaims.AddRange(
            new CustomCommandInvocationClaim
            {
                HostId = a,
                CustomCommandId = command.Id,
                TwitchUserId = _aliceId,
                ClaimedAtUtc = now,
            },
            new CustomCommandInvocationClaim
            {
                HostId = a,
                CustomCommandId = command.Id,
                TwitchUserId = _bobId,
                ClaimedAtUtc = now,
            }
        );
        _ = db.CustomCommandInvocationResetAudits.Add(
            new CustomCommandInvocationResetAudit
            {
                HostId = a,
                CustomCommandId = command.Id,
                CommandName = "hype",
                ActorTwitchUserId = _aliceId,
                ActorLogin = _alice,
                Scope = CustomCommandInvocationResetScope.OneViewer,
                TargetTwitchUserId = _aliceId,
                TargetLogin = _alice,
                AffectedClaimCount = 3,
                ResetAtUtc = now,
            }
        );

        _ = db.DurableAlerts.Add(
            new DurableAlert
            {
                HostId = a,
                Source = "test",
                SourceKey = "test",
                Title = "Alert",
                Message = "Something needed attention.",
                CreatedAtUtc = now,
                AcknowledgedAtUtc = now,
                AcknowledgedByLogin = _alice,
            }
        );
        db.SiteAccessEntries.AddRange(
            new SiteAccessEntry
            {
                Login = _alice,
                Kind = AccessListEntryKind.Whitelist,
                CreatedAtUtc = now,
            },
            new SiteAccessEntry
            {
                Login = _bob,
                Kind = AccessListEntryKind.Whitelist,
                CreatedAtUtc = now,
            }
        );
        db.HostModAccessEntries.AddRange(
            new HostModAccessEntry
            {
                HostId = a,
                Login = _alice,
                Kind = AccessListEntryKind.Whitelist,
                CreatedAtUtc = now,
            },
            new HostModAccessEntry
            {
                HostId = b,
                Login = _alice,
                Kind = AccessListEntryKind.Whitelist,
                CreatedAtUtc = now,
            }
        );

        _ = db.WhisperQuotaBuckets.Add(
            new WhisperQuotaBucket
            {
                HostId = a,
                BotTwitchUserId = "555",
                DayUtc = now.Date,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Recipients =
                [
                    new WhisperQuotaRecipient
                    {
                        RecipientTwitchUserId = _aliceId,
                        RecipientLogin = _alice,
                        FirstSentAtUtc = now,
                    },
                    new WhisperQuotaRecipient
                    {
                        RecipientTwitchUserId = _bobId,
                        RecipientLogin = _bob,
                        FirstSentAtUtc = now,
                    },
                ],
            }
        );

        db.ShoutoutHistory.AddRange(
            new ShoutoutHistoryEntry
            {
                HostId = a,
                Direction = ShoutoutHistoryDirection.Sent,
                SourceTwitchUserId = "9001",
                SourceLogin = "streamer-a",
                TargetTwitchUserId = _aliceId,
                TargetLogin = _alice,
                OccurredAtUtc = now,
            },
            new ShoutoutHistoryEntry
            {
                HostId = a,
                Direction = ShoutoutHistoryDirection.Sent,
                SourceTwitchUserId = "9001",
                SourceLogin = "streamer-a",
                TargetTwitchUserId = _bobId,
                TargetLogin = _bob,
                OccurredAtUtc = now,
            }
        );
        _ = db.ShoutoutCooldowns.Add(
            new ShoutoutCooldownState
            {
                HostId = a,
                TargetTwitchUserId = _aliceId,
                TargetLogin = _alice,
                TargetEligibleAtUtc = now.AddMinutes(5),
                UpdatedAtUtc = now,
            }
        );
        db.AutomaticRaidShoutoutOutcomes.AddRange(
            new AutomaticRaidShoutoutOutcome
            {
                HostId = a,
                ProviderMessageId = "raid-1",
                SourceTwitchUserId = _aliceId,
                SourceLogin = _alice,
                SourceDisplayName = "Alice",
                ViewerCount = 5,
                MessageTimestampUtc = now,
                ClaimedAtUtc = now,
            },
            new AutomaticRaidShoutoutOutcome
            {
                HostId = a,
                ProviderMessageId = "raid-2",
                SourceTwitchUserId = _bobId,
                SourceLogin = _bob,
                SourceDisplayName = "Bob",
                ViewerCount = 5,
                MessageTimestampUtc = now,
                ClaimedAtUtc = now,
            }
        );

        db.TwitchRewardRedemptions.AddRange(
            new TwitchRewardRedemption
            {
                HostId = a,
                ProviderRedemptionId = "redemption-1",
                ProviderRewardId = "reward-1",
                RewardTitle = "Hydrate",
                UserId = _aliceId,
                UserLogin = _alice,
                UserInput = "hello from alice",
                RedeemedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new TwitchRewardRedemption
            {
                HostId = a,
                ProviderRedemptionId = "redemption-2",
                ProviderRewardId = "reward-1",
                RewardTitle = "Hydrate",
                UserId = _bobId,
                UserLogin = _bob,
                UserInput = string.Empty,
                RedeemedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = db.TwitchClips.Add(
            new TwitchClip
            {
                HostId = a,
                IdempotencyKey = "clip-1",
                Status = TwitchClipStatus.Available,
                BroadcasterTwitchUserId = "9001",
                BroadcasterLogin = "streamer-a",
                CreatorTwitchUserId = _aliceId,
                CreatorLogin = _alice,
                RequestedAtUtc = now,
            }
        );

        var board = new RequestBoard
        {
            HostId = a,
            Slug = "songs",
            Title = "Song requests",
            IsOpen = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Fields =
            [
                new RequestBoardField
                {
                    Position = 0,
                    Key = "title",
                    Label = "Title",
                    Kind = RequestBoardFieldKind.Text,
                    IsRequired = true,
                },
            ],
        };
        _ = db.RequestBoards.Add(board);
        _ = await db.SaveChangesAsync();
        var aliceSubmission = new RequestSubmission
        {
            HostId = a,
            BoardId = board.Id,
            OperationId = Guid.NewGuid(),
            SubmitterLogin = _alice,
            Title = "Alice's song",
            NormalizedTitle = "alice's song",
            Status = RequestSubmissionStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Values =
            [
                new RequestSubmissionValue
                {
                    FieldId = board.Fields[0].Id,
                    Value = "A song alice likes",
                },
            ],
        };
        var bobSubmission = new RequestSubmission
        {
            HostId = a,
            BoardId = board.Id,
            OperationId = Guid.NewGuid(),
            SubmitterLogin = _bob,
            Title = "Bob's song",
            NormalizedTitle = "bob's song",
            Status = RequestSubmissionStatus.Pending,
            VoteCount = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.RequestSubmissions.AddRange(aliceSubmission, bobSubmission);
        _ = await db.SaveChangesAsync();
        _ = db.RequestSubmissionVotes.Add(
            new RequestSubmissionVote
            {
                SubmissionId = bobSubmission.Id,
                VoterLogin = _alice,
                CreatedAtUtc = now,
            }
        );
        db.RequestBoardEvents.AddRange(
            new RequestBoardDomainEvent
            {
                HostId = a,
                BoardId = board.Id,
                SubmissionId = aliceSubmission.Id,
                SchemaVersion = 1,
                Kind = RequestBoardEventKind.Submitted,
                PublicPayload = $$"""{"submitter":"{{_alice}}","title":"Alice's song"}""",
                OccurredAtUtc = now,
            },
            new RequestBoardDomainEvent
            {
                HostId = a,
                BoardId = board.Id,
                SubmissionId = bobSubmission.Id,
                SchemaVersion = 1,
                Kind = RequestBoardEventKind.Submitted,
                PublicPayload = $$"""{"submitter":"{{_bob}}","title":"Bob's song"}""",
                OccurredAtUtc = now,
            }
        );

        var queue = new PlayQueue
        {
            HostId = a,
            Slug = "games",
            Name = "Games",
            ActivityName = "Rounds",
            IsOpen = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Fields =
            [
                new PlayQueueField
                {
                    Position = 0,
                    Key = "answer",
                    Label = "Answer",
                },
            ],
        };
        _ = db.PlayQueues.Add(queue);
        _ = await db.SaveChangesAsync();
        db.PlayQueueEntries.AddRange(
            new PlayQueueEntry
            {
                HostId = a,
                QueueId = queue.Id,
                IdentityKey = $"id:{_aliceId}",
                TwitchUserId = _aliceId,
                NormalizedLogin = _alice,
                DisplayName = "Alice",
                Status = PlayQueueEntryStatus.Waiting,
                JoinedAtUtc = now,
                UpdatedAtUtc = now,
                Values = [new PlayQueueEntryValue { FieldId = queue.Fields[0].Id, Value = "42" }],
            },
            new PlayQueueEntry
            {
                HostId = a,
                QueueId = queue.Id,
                IdentityKey = $"id:{_bobId}",
                TwitchUserId = _bobId,
                NormalizedLogin = _bob,
                DisplayName = "Bob",
                Status = PlayQueueEntryStatus.Waiting,
                JoinedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = db.PlayQueueParticipation.Add(
            new PlayQueueParticipation
            {
                HostId = a,
                QueueId = queue.Id,
                IdentityKey = $"login:{_alice}",
                ParticipatedAtUtc = now,
            }
        );
        _ = db.PlayQueueExclusions.Add(
            new PlayQueueExclusion
            {
                HostId = a,
                QueueId = queue.Id,
                IdentityKey = $"id:{_aliceId}",
                ExpiresAtUtc = now.AddMinutes(15),
                PrivateReason = "skipped twice",
            }
        );
        db.PlayQueueEvents.AddRange(
            new PlayQueueDomainEvent
            {
                HostId = a,
                QueueId = queue.Id,
                SchemaVersion = 1,
                Kind = PlayQueueEventKind.Joined,
                PublicPayload = $$"""{"viewer":"{{_alice}}"}""",
                OccurredAtUtc = now,
            },
            new PlayQueueDomainEvent
            {
                HostId = a,
                QueueId = queue.Id,
                SchemaVersion = 1,
                Kind = PlayQueueEventKind.Joined,
                PublicPayload = $$"""{"viewer":"{{_bob}}"}""",
                OccurredAtUtc = now,
            }
        );

        var candidate = new MomentCandidate
        {
            HostId = a,
            PublicId = Guid.NewGuid(),
            StreamIdentity = "stream-1",
            State = MomentCandidateState.Approved,
            PublicTitle = "Great play",
            CapturedAtUtc = now,
            LastCapturedAtUtc = now,
            Contributors =
            [
                new MomentContributor
                {
                    IdentityKey = $"id:{_aliceId}",
                    TwitchUserId = _aliceId,
                    NormalizedLogin = _alice,
                    DisplayName = "Alice",
                    CaptureCount = 1,
                    FirstCapturedAtUtc = now,
                    LastCapturedAtUtc = now,
                },
                new MomentContributor
                {
                    IdentityKey = $"id:{_bobId}",
                    TwitchUserId = _bobId,
                    NormalizedLogin = _bob,
                    DisplayName = "Bob",
                    CaptureCount = 1,
                    FirstCapturedAtUtc = now,
                    LastCapturedAtUtc = now,
                },
            ],
            CaptureRequests =
            [
                new MomentCaptureRequest { IdentityKey = $"id:{_aliceId}", CapturedAtUtc = now },
            ],
            Suggestions =
            [
                new MomentSuggestion
                {
                    IdentityKey = $"id:{_aliceId}",
                    SuggestedTitle = "Alice's title",
                    SuggestedCategory = "Hype",
                    CreatedAtUtc = now,
                },
            ],
            Votes =
            [
                new MomentVote
                {
                    IdentityKey = $"id:{_aliceId}",
                    TwitchUserId = _aliceId,
                    NormalizedLogin = _alice,
                    CreatedAtUtc = now,
                },
            ],
        };
        _ = db.MomentCandidates.Add(candidate);
        _ = await db.SaveChangesAsync();
        _ = db.MomentModerationAudit.Add(
            new MomentModerationAudit
            {
                HostId = a,
                CandidateId = candidate.Id,
                Action = "approve",
                ActorLogin = _alice,
                PrivateText = "approved by alice",
                OccurredAtUtc = now,
            }
        );
        _ = db.MomentMerges.Add(
            new MomentMerge
            {
                HostId = a,
                SourceCandidateId = candidate.Id,
                TargetCandidateId = candidate.Id,
                ActorLogin = _alice,
                PrivateText = "merged duplicates",
                MergedAtUtc = now,
            }
        );
        db.MomentEvents.AddRange(
            new MomentDomainEvent
            {
                HostId = a,
                CandidateId = candidate.Id,
                SchemaVersion = 1,
                Kind = MomentEventKind.Captured,
                StreamIdentity = "stream-1",
                PublicPayload = $$"""{"contributor":"{{_alice}}"}""",
                OccurredAtUtc = now,
            },
            new MomentDomainEvent
            {
                HostId = a,
                CandidateId = candidate.Id,
                SchemaVersion = 1,
                Kind = MomentEventKind.Captured,
                StreamIdentity = "stream-1",
                PublicPayload = $$"""{"contributor":"{{_bob}}"}""",
                OccurredAtUtc = now,
            }
        );

        var overlay = new OverlayInstance
        {
            HostId = a,
            PublicId = Guid.NewGuid(),
            Name = "Feed",
            Type = OverlayType.EventFeed,
            IsEnabled = true,
            ConfigurationJson = """{"schemaVersion":1}""",
            AccessKeyDigest = new byte[32],
            KeyVersion = 1,
            Revision = 1,
        };
        _ = db.OverlayInstances.Add(overlay);
        _ = await db.SaveChangesAsync();
        _ = db.OverlayInstanceEvents.Add(
            new OverlayInstanceDomainEvent
            {
                HostId = a,
                OverlayPublicId = overlay.PublicId,
                SchemaVersion = 1,
                Kind = OverlayInstanceEventKind.Created,
                ActorUserId = _aliceId,
                ActorLogin = _alice,
                OccurredAtUtc = now,
            }
        );
        db.OverlayEventFeedItems.AddRange(
            new OverlayEventFeedItem
            {
                OverlayInstanceId = overlay.Id,
                HostId = a,
                Kind = OverlayEventFeedKind.PointAward,
                SourceKey = "feed-1",
                Priority = OverlayEventFeedPriority.Normal,
                Lifecycle = OverlayEventFeedLifecycle.Queued,
                Title = "Moment captured",
                Body = $"{_alice} captured a moment",
                DurationSeconds = 5,
                EnqueuedAtUtc = now,
            },
            new OverlayEventFeedItem
            {
                OverlayInstanceId = overlay.Id,
                HostId = a,
                Kind = OverlayEventFeedKind.PointAward,
                SourceKey = "feed-2",
                Priority = OverlayEventFeedPriority.Normal,
                Lifecycle = OverlayEventFeedLifecycle.Queued,
                Title = "Moment captured",
                Body = $"{_bob} captured a moment",
                DurationSeconds = 5,
                EnqueuedAtUtc = now,
            }
        );

        _ = db.PublicChatPinOperations.Add(
            new PublicChatPinOperation
            {
                Kind = PublicChatPinOperationKind.Pin,
                Status = PublicChatPinOperationStatus.Succeeded,
                HostId = a,
                Channel = "streamer-a",
                Feature = "guessing",
                ReplyKey = "round",
                OwnerId = 1,
                TwitchMessageId = "msg-1",
                PinnerTwitchUserId = _aliceId,
                CreatedAtUtc = now,
            }
        );
        _ = db.ActivePublicChatPins.Add(
            new ActivePublicChatPin
            {
                HostId = a,
                Channel = "streamer-a",
                TwitchMessageId = "msg-1",
                PinnerTwitchUserId = _aliceId,
                Feature = "guessing",
                ReplyKey = "round",
                OwnerId = 1,
                PinnedAtUtc = now,
            }
        );

        var flow = new AutomationFlow
        {
            Id = Guid.NewGuid(),
            HostId = a,
            Name = "Welcome",
            SchemaVersion = 1,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _ = db.AutomationFlows.Add(flow);
        _ = await db.SaveChangesAsync();
        var aliceRun = new AutomationFlowRun
        {
            Id = Guid.NewGuid(),
            FlowId = flow.Id,
            HostId = a,
            AutomationGeneration = 1,
            RequiredFeatures = HostFeatureFlags.None,
            ContextSchemaVersion = 1,
            SourceDefinitionId = "chat.message",
            SourceOccurrenceId = Guid.NewGuid(),
            ContextJson = $$"""{"userLogin":"{{_alice}}","userId":"{{_aliceId}}"}""",
            DefinitionJson = "{}",
            Status = AutomationFlowRunStatus.Completed,
            StartedAtUtc = now,
            CompletedAtUtc = now,
        };
        var bobRun = new AutomationFlowRun
        {
            Id = Guid.NewGuid(),
            FlowId = flow.Id,
            HostId = a,
            AutomationGeneration = 1,
            RequiredFeatures = HostFeatureFlags.None,
            ContextSchemaVersion = 1,
            SourceDefinitionId = "chat.message",
            SourceOccurrenceId = Guid.NewGuid(),
            ContextJson = $$"""{"userLogin":"{{_bob}}","userId":"{{_bobId}}"}""",
            DefinitionJson = "{}",
            Status = AutomationFlowRunStatus.Completed,
            StartedAtUtc = now,
            CompletedAtUtc = now,
        };
        db.AutomationFlowRuns.AddRange(aliceRun, bobRun);
        db.AutomationNodeRuns.AddRange(
            new AutomationNodeRun
            {
                RunId = aliceRun.Id,
                NodeId = Guid.NewGuid(),
                Sequence = 1,
                Status = AutomationNodeRunStatus.Succeeded,
                AvailableAtUtc = now,
            },
            new AutomationNodeRun
            {
                RunId = bobRun.Id,
                NodeId = Guid.NewGuid(),
                Sequence = 1,
                Status = AutomationNodeRunStatus.Succeeded,
                AvailableAtUtc = now,
            }
        );

        _ = await db.SaveChangesAsync();
        return new Fixture(a, b, aliceLedger.Id, bobLedger.Id);
    }
}
