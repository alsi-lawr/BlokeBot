using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointsConfigurationCommandTests
{
    [Test]
    public void MutableDraft_Validating_ProducesNormalizedCopyIsolatedCommand()
    {
        var draft = new PointsConfiguration
        {
            PointLabel = " channel points ",
            GamblingCooldownSeconds = 0,
            GiveawayDurationSeconds = 1,
            GiveawayWinnerCount = 1,
            GiveawayCooldownSeconds = PointsConfigurationValidator.MinimumGiveawayCooldownSeconds,
        };
        draft.Aliases.PointsAliases = " Score, POINTS ";
        draft.Replies.BalanceReply = " Balance: {balance}. ";
        foreach (var replyKey in PointsReplyKeys.WhisperableKeys)
        {
            draft.ReplyDelivery.DeliverAsWhisper(replyKey);
        }

        var command = ValidCommand(draft);
        draft.PointLabel = "mutated";
        draft.Aliases.PointsAliases = "mutated";
        draft.Replies.BalanceReply = "mutated";
        draft.ReplyDelivery.DeliverInChat(PointsReplyKeys.Balance);

        command.PointLabel.ShouldBe("channel points");
        command.Aliases.PointsAliases.ShouldBe("points, score");
        command.Replies.BalanceReply.ShouldBe("Balance: {balance}.");
        command.GamblingCooldownSeconds.ShouldBe(0);
        command.GiveawayDurationSeconds.ShouldBe(1);
        command.GiveawayWinnerCount.ShouldBe(1);
        command.GiveawayCooldownSeconds.ShouldBe(
            PointsConfigurationValidator.MinimumGiveawayCooldownSeconds
        );
        command.ReplyDelivery.WhisperKeys.Count.ShouldBe(PointsReplyKeys.WhisperableKeys.Count);
        foreach (var replyKey in PointsReplyKeys.WhisperableKeys)
        {
            command.ReplyDelivery.IsWhisper(replyKey).ShouldBeTrue();
        }
    }

    [Test]
    public void InvalidDraft_Validating_ReturnsTypedErrorsWithoutMutatingDraft()
    {
        var draft = new PointsConfiguration
        {
            GamblingWinRatePercent = 101,
            GamblingCooldownSeconds = -4,
            GiveawayDurationSeconds = 0,
            GiveawayMinimumPayout = "not-a-number",
            GiveawayWinnerCount = 0,
            GiveawayCooldownSeconds = 299,
        };
        draft.Aliases.PointsAliases = "shared";
        draft.Aliases.GivePointsAliases = "SHARED";

        var errors = PointsConfigurationValidator
            .Validate(draft)
            .Match(
                _ => Array.Empty<PointsConfigurationValidationError>(),
                invalid => invalid.ToArray()
            );

        errors.ShouldContain(new PointsConfigurationValidationError.InvalidMinimumPayout());
        errors.ShouldContain(new PointsConfigurationValidationError.InvalidGamblingWinRate());
        errors.ShouldContain(new PointsConfigurationValidationError.NegativeGamblingCooldown());
        errors.ShouldContain(new PointsConfigurationValidationError.GiveawayDurationBelowMinimum());
        errors.ShouldContain(
            new PointsConfigurationValidationError.GiveawayWinnerCountBelowMinimum()
        );
        errors.ShouldContain(new PointsConfigurationValidationError.GiveawayCooldownBelowMinimum());
        errors.ShouldContain(new PointsConfigurationValidationError.DuplicateAlias("shared"));
        draft.GamblingCooldownSeconds.ShouldBe(-4);
        draft.GiveawayDurationSeconds.ShouldBe(0);
        draft.GiveawayMinimumPayout.ShouldBe("not-a-number");
        draft.GiveawayWinnerCount.ShouldBe(0);
        draft.GiveawayCooldownSeconds.ShouldBe(299);
        draft.Aliases.PointsAliases.ShouldBe("shared");
    }

    [Test]
    public async Task AliasOwnedByAnotherFeature_Saving_ReturnsTypedFailureWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(dbFactory, "shared");
        var service = CreateService(dbFactory);
        var draft = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        draft.Aliases.PointsAliases = "shared";

        var result = await service
            .SaveConfiguration(hostId, ValidCommand(draft))
            .ExecuteAsync(CancellationToken.None);
        var failure = result.Match<PointsConfigurationSaveFailure?>(_ => null, error => error);

        failure.ShouldNotBeNull();
        failure.ShouldBe(new PointsConfigurationSaveFailure("shared"));
        failure.Message.ShouldBe("!shared is already used by another bot command.");
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointsSettings.CountAsync()).ShouldBe(0);
        (await db.CommandAliases.SingleAsync()).Alias.ShouldBe("shared");
    }

    [Test]
    public async Task AliasOwnedByCustomCommand_Saving_ReturnsTypedFailureWithoutMutation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(dbFactory, "existing-points");
        await SeedCustomCommandAliasAsync(dbFactory, hostId, "shared");
        var service = CreateService(dbFactory);
        var draft = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        draft.Aliases.PointsAliases = "shared";

        var result = await service
            .SaveConfiguration(hostId, ValidCommand(draft))
            .ExecuteAsync(CancellationToken.None);
        var failure = result.Match<PointsConfigurationSaveFailure?>(_ => null, error => error);

        failure.ShouldBe(new PointsConfigurationSaveFailure("shared"));
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointsSettings.CountAsync()).ShouldBe(0);
        (await db.CommandAliases.SingleAsync()).Alias.ShouldBe("existing-points");
        (await db.CustomCommandAliases.SingleAsync()).Alias.ShouldBe("shared");
    }

    [Test]
    public async Task CancelledExecution_Saving_DoesNotStartPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateService(dbFactory);
        var command = ValidCommand(new PointsConfiguration());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            service.SaveConfiguration(1, command).ExecuteAsync(cancellation.Token).AsTask()
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointsSettings.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task FullyPopulatedCommand_MutatingDraftBeforeExecution_PersistsSnapshot()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var service = CreateService(dbFactory);
        var draft = FullyPopulatedDraft();
        var command = ValidCommand(draft);

        MutateEverySubmittedDraftValue(draft);
        AssertExpectedSnapshot(command);

        var result = await service
            .SaveConfiguration(hostId, command)
            .ExecuteAsync(CancellationToken.None);
        result.Match(
            static _ => true,
            failure => throw new InvalidOperationException(failure.Message)
        );
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);

        AssertLoadedConfiguration(loaded, command);
    }

    private static PointsConfiguration FullyPopulatedDraft()
    {
        var draft = new PointsConfiguration
        {
            PointLabel = " channel points ",
            Aliases = new()
            {
                PointsAliases = " !POINTS-CHECK ",
                GivePointsAliases = " !GIVE-CHECK ",
                AddPointsAliases = " !ADD-CHECK ",
                RemovePointsAliases = " !REMOVE-CHECK ",
                GambleAliases = " !GAMBLE-CHECK ",
                GiveawayAliases = " !GIVEAWAY-CHECK ",
                JoinAliases = " !JOIN-CHECK ",
                EndGiveawayAliases = " !END-CHECK ",
                CancelGiveawayAliases = " !CANCEL-CHECK ",
            },
            Replies = new()
            {
                BalanceReply = " Balance reply. ",
                OtherBalanceReply = " Other balance reply. ",
                TransferReply = " Transfer reply. ",
                AddReply = " Add reply. ",
                RemoveReply = " Remove reply. ",
                InvalidAmountReply = " Invalid amount reply. ",
                InsufficientBalanceReply = " Insufficient balance reply. ",
                ModeratorOnlyReply = " Moderator only reply. ",
                GamblingWinReply = " Gambling win reply. ",
                GamblingLoseReply = " Gambling lose reply. ",
                GiveawayStartedReply = " Giveaway started reply. ",
                GiveawayUpdateReply = " Giveaway update reply. ",
                GiveawayJoinedReply = " Giveaway joined reply. ",
                GiveawayAlreadyJoinedReply = " Giveaway already joined reply. ",
                GiveawayEndedReply = " Giveaway ended reply. ",
                GiveawayNoEntrantsReply = " Giveaway no entrants reply. ",
                GiveawayCancelledReply = " Giveaway cancelled reply. ",
                GiveawayAlreadyActiveReply = " Giveaway already active reply. ",
                GiveawayNotActiveReply = " Giveaway not active reply. ",
                GiveawayCooldownReply = " Giveaway cooldown reply. ",
                StreamOfflineReply = " Stream offline reply. ",
                NotEligibleReply = " Not eligible reply. ",
                FollowerEligibilityUnavailableReply = " Follower eligibility unavailable reply. ",
            },
            GamblingWinRatePercent = 63,
            GamblingCooldownSeconds = 10,
            GiveawayDurationSeconds = 120,
            GiveawayMinimumPayout = " 20 ",
            GiveawayMaximumPayout = " 250 ",
            GiveawayWinnerCount = 3,
            GiveawayEligibility = PointsEligibilityMode.Followers,
            GiveawayCooldownSeconds = 600,
        };
        foreach (var replyKey in PointsReplyKeys.WhisperableKeys)
        {
            draft.ReplyDelivery.DeliverAsWhisper(replyKey);
        }

        return draft;
    }

    private static void MutateEverySubmittedDraftValue(PointsConfiguration draft)
    {
        draft.PointLabel = "mutated label";
        draft.Aliases.PointsAliases = "mutated-points";
        draft.Aliases.GivePointsAliases = "mutated-give";
        draft.Aliases.AddPointsAliases = "mutated-add";
        draft.Aliases.RemovePointsAliases = "mutated-remove";
        draft.Aliases.GambleAliases = "mutated-gamble";
        draft.Aliases.GiveawayAliases = "mutated-giveaway";
        draft.Aliases.JoinAliases = "mutated-join";
        draft.Aliases.EndGiveawayAliases = "mutated-end";
        draft.Aliases.CancelGiveawayAliases = "mutated-cancel";
        draft.Replies.BalanceReply = "mutated balance";
        draft.Replies.OtherBalanceReply = "mutated other balance";
        draft.Replies.TransferReply = "mutated transfer";
        draft.Replies.AddReply = "mutated add";
        draft.Replies.RemoveReply = "mutated remove";
        draft.Replies.InvalidAmountReply = "mutated invalid amount";
        draft.Replies.InsufficientBalanceReply = "mutated insufficient balance";
        draft.Replies.ModeratorOnlyReply = "mutated moderator only";
        draft.Replies.GamblingWinReply = "mutated gambling win";
        draft.Replies.GamblingLoseReply = "mutated gambling lose";
        draft.Replies.GiveawayStartedReply = "mutated giveaway started";
        draft.Replies.GiveawayUpdateReply = "mutated giveaway update";
        draft.Replies.GiveawayJoinedReply = "mutated giveaway joined";
        draft.Replies.GiveawayAlreadyJoinedReply = "mutated giveaway already joined";
        draft.Replies.GiveawayEndedReply = "mutated giveaway ended";
        draft.Replies.GiveawayNoEntrantsReply = "mutated giveaway no entrants";
        draft.Replies.GiveawayCancelledReply = "mutated giveaway cancelled";
        draft.Replies.GiveawayAlreadyActiveReply = "mutated giveaway already active";
        draft.Replies.GiveawayNotActiveReply = "mutated giveaway not active";
        draft.Replies.GiveawayCooldownReply = "mutated giveaway cooldown";
        draft.Replies.StreamOfflineReply = "mutated stream offline";
        draft.Replies.NotEligibleReply = "mutated not eligible";
        draft.Replies.FollowerEligibilityUnavailableReply =
            "mutated follower eligibility unavailable";
        foreach (var replyKey in PointsReplyKeys.WhisperableKeys)
        {
            draft.ReplyDelivery.DeliverInChat(replyKey);
        }

        draft.GamblingWinRatePercent = 10;
        draft.GamblingCooldownSeconds = 90;
        draft.GiveawayDurationSeconds = 900;
        draft.GiveawayMinimumPayout = "1000";
        draft.GiveawayMaximumPayout = "2000";
        draft.GiveawayWinnerCount = 9;
        draft.GiveawayEligibility = PointsEligibilityMode.Subscribers;
        draft.GiveawayCooldownSeconds = 900;
    }

    private static void AssertExpectedSnapshot(PointsConfigurationSaveCommand command)
    {
        command.PointLabel.ShouldBe("channel points");
        command.Aliases.PointsAliases.ShouldBe("points-check");
        command.Aliases.GivePointsAliases.ShouldBe("give-check");
        command.Aliases.AddPointsAliases.ShouldBe("add-check");
        command.Aliases.RemovePointsAliases.ShouldBe("remove-check");
        command.Aliases.GambleAliases.ShouldBe("gamble-check");
        command.Aliases.GiveawayAliases.ShouldBe("giveaway-check");
        command.Aliases.JoinAliases.ShouldBe("join-check");
        command.Aliases.EndGiveawayAliases.ShouldBe("end-check");
        command.Aliases.CancelGiveawayAliases.ShouldBe("cancel-check");
        command.Replies.BalanceReply.ShouldBe("Balance reply.");
        command.Replies.OtherBalanceReply.ShouldBe("Other balance reply.");
        command.Replies.TransferReply.ShouldBe("Transfer reply.");
        command.Replies.AddReply.ShouldBe("Add reply.");
        command.Replies.RemoveReply.ShouldBe("Remove reply.");
        command.Replies.InvalidAmountReply.ShouldBe("Invalid amount reply.");
        command.Replies.InsufficientBalanceReply.ShouldBe("Insufficient balance reply.");
        command.Replies.ModeratorOnlyReply.ShouldBe("Moderator only reply.");
        command.Replies.GamblingWinReply.ShouldBe("Gambling win reply.");
        command.Replies.GamblingLoseReply.ShouldBe("Gambling lose reply.");
        command.Replies.GiveawayStartedReply.ShouldBe("Giveaway started reply.");
        command.Replies.GiveawayUpdateReply.ShouldBe("Giveaway update reply.");
        command.Replies.GiveawayJoinedReply.ShouldBe("Giveaway joined reply.");
        command.Replies.GiveawayAlreadyJoinedReply.ShouldBe("Giveaway already joined reply.");
        command.Replies.GiveawayEndedReply.ShouldBe("Giveaway ended reply.");
        command.Replies.GiveawayNoEntrantsReply.ShouldBe("Giveaway no entrants reply.");
        command.Replies.GiveawayCancelledReply.ShouldBe("Giveaway cancelled reply.");
        command.Replies.GiveawayAlreadyActiveReply.ShouldBe("Giveaway already active reply.");
        command.Replies.GiveawayNotActiveReply.ShouldBe("Giveaway not active reply.");
        command.Replies.GiveawayCooldownReply.ShouldBe("Giveaway cooldown reply.");
        command.Replies.StreamOfflineReply.ShouldBe("Stream offline reply.");
        command.Replies.NotEligibleReply.ShouldBe("Not eligible reply.");
        command.Replies.FollowerEligibilityUnavailableReply.ShouldBe(
            "Follower eligibility unavailable reply."
        );
        command.GamblingWinRatePercent.ShouldBe(63);
        command.GamblingCooldownSeconds.ShouldBe(10);
        command.GiveawayDurationSeconds.ShouldBe(120);
        command.GiveawayMinimumPayout.ToString().ShouldBe("20");
        command.GiveawayMaximumPayout.ToString().ShouldBe("250");
        command.GiveawayWinnerCount.ShouldBe(3);
        command.GiveawayEligibility.ShouldBe(PointsEligibilityMode.Followers);
        command.GiveawayCooldownSeconds.ShouldBe(600);
        foreach (var replyKey in PointsReplyKeys.WhisperableKeys)
        {
            command.ReplyDelivery.IsWhisper(replyKey).ShouldBeTrue();
        }
    }

    private static void AssertLoadedConfiguration(
        PointsConfiguration loaded,
        PointsConfigurationSaveCommand command
    )
    {
        loaded.PointLabel.ShouldBe(command.PointLabel);
        loaded.Aliases.PointsAliases.ShouldBe(command.Aliases.PointsAliases);
        loaded.Aliases.GivePointsAliases.ShouldBe(command.Aliases.GivePointsAliases);
        loaded.Aliases.AddPointsAliases.ShouldBe(command.Aliases.AddPointsAliases);
        loaded.Aliases.RemovePointsAliases.ShouldBe(command.Aliases.RemovePointsAliases);
        loaded.Aliases.GambleAliases.ShouldBe(command.Aliases.GambleAliases);
        loaded.Aliases.GiveawayAliases.ShouldBe(command.Aliases.GiveawayAliases);
        loaded.Aliases.JoinAliases.ShouldBe(command.Aliases.JoinAliases);
        loaded.Aliases.EndGiveawayAliases.ShouldBe(command.Aliases.EndGiveawayAliases);
        loaded.Aliases.CancelGiveawayAliases.ShouldBe(command.Aliases.CancelGiveawayAliases);
        loaded.Replies.BalanceReply.ShouldBe(command.Replies.BalanceReply);
        loaded.Replies.OtherBalanceReply.ShouldBe(command.Replies.OtherBalanceReply);
        loaded.Replies.TransferReply.ShouldBe(command.Replies.TransferReply);
        loaded.Replies.AddReply.ShouldBe(command.Replies.AddReply);
        loaded.Replies.RemoveReply.ShouldBe(command.Replies.RemoveReply);
        loaded.Replies.InvalidAmountReply.ShouldBe(command.Replies.InvalidAmountReply);
        loaded.Replies.InsufficientBalanceReply.ShouldBe(command.Replies.InsufficientBalanceReply);
        loaded.Replies.ModeratorOnlyReply.ShouldBe(command.Replies.ModeratorOnlyReply);
        loaded.Replies.GamblingWinReply.ShouldBe(command.Replies.GamblingWinReply);
        loaded.Replies.GamblingLoseReply.ShouldBe(command.Replies.GamblingLoseReply);
        loaded.Replies.GiveawayStartedReply.ShouldBe(command.Replies.GiveawayStartedReply);
        loaded.Replies.GiveawayUpdateReply.ShouldBe(command.Replies.GiveawayUpdateReply);
        loaded.Replies.GiveawayJoinedReply.ShouldBe(command.Replies.GiveawayJoinedReply);
        loaded.Replies.GiveawayAlreadyJoinedReply.ShouldBe(
            command.Replies.GiveawayAlreadyJoinedReply
        );
        loaded.Replies.GiveawayEndedReply.ShouldBe(command.Replies.GiveawayEndedReply);
        loaded.Replies.GiveawayNoEntrantsReply.ShouldBe(command.Replies.GiveawayNoEntrantsReply);
        loaded.Replies.GiveawayCancelledReply.ShouldBe(command.Replies.GiveawayCancelledReply);
        loaded.Replies.GiveawayAlreadyActiveReply.ShouldBe(
            command.Replies.GiveawayAlreadyActiveReply
        );
        loaded.Replies.GiveawayNotActiveReply.ShouldBe(command.Replies.GiveawayNotActiveReply);
        loaded.Replies.GiveawayCooldownReply.ShouldBe(command.Replies.GiveawayCooldownReply);
        loaded.Replies.StreamOfflineReply.ShouldBe(command.Replies.StreamOfflineReply);
        loaded.Replies.NotEligibleReply.ShouldBe(command.Replies.NotEligibleReply);
        loaded.Replies.FollowerEligibilityUnavailableReply.ShouldBe(
            command.Replies.FollowerEligibilityUnavailableReply
        );
        loaded.GamblingWinRatePercent.ShouldBe(command.GamblingWinRatePercent);
        loaded.GamblingCooldownSeconds.ShouldBe(command.GamblingCooldownSeconds);
        loaded.GiveawayDurationSeconds.ShouldBe(command.GiveawayDurationSeconds);
        loaded.GiveawayMinimumPayout.ShouldBe(command.GiveawayMinimumPayout.ToString());
        loaded.GiveawayMaximumPayout.ShouldBe(command.GiveawayMaximumPayout.ToString());
        loaded.GiveawayWinnerCount.ShouldBe(command.GiveawayWinnerCount);
        loaded.GiveawayEligibility.ShouldBe(command.GiveawayEligibility);
        loaded.GiveawayCooldownSeconds.ShouldBe(command.GiveawayCooldownSeconds);
        foreach (var replyKey in PointsReplyKeys.WhisperableKeys)
        {
            loaded.ReplyDelivery.IsWhisper(replyKey).ShouldBeTrue();
        }
    }

    private static PointsConfigurationSaveCommand ValidCommand(PointsConfiguration draft) =>
        PointsConfigurationValidator
            .Validate(draft)
            .Match(
                command => command,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
            );

    private static PointsConfigurationService CreateService(SqliteBlokeBotDbFactory dbFactory) =>
        new(dbFactory, new PointsChangeNotifier(TestEventBus.Create<AppEventKind>()));

    private static async Task<int> SeedHostWithAliasAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string alias
    )
    {
        var hostId = await SeedHostAsync(dbFactory);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = hostId,
                Kind = AppCommandKind.Start,
                Alias = alias,
            }
        );
        await db.SaveChangesAsync();
        return hostId;
    }

    private static async Task SeedCustomCommandAliasAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string alias
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        db.CustomCommands.Add(
            new CustomCommand
            {
                HostId = hostId,
                Name = "Existing custom command",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Aliases = [new CustomCommandAlias { HostId = hostId, Alias = alias }],
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
