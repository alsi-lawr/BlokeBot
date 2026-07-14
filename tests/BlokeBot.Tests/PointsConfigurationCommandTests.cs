using BlokeBot.Eventing;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Replies;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PointsConfigurationCommandTests
{
    [Test]
    public void MutableDraft_Validating_ProducesNormalizedCopyIsolatedCommand()
    {
        var draft = new PointsConfiguration
        {
            PointLabel = " channel points ",
            GamblingCooldownSeconds = -10,
            GiveawayDurationSeconds = 0,
            GiveawayWinnerCount = 0,
            GiveawayCooldownSeconds = 1,
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
            GiveawayMinimumPayout = "not-a-number",
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
        errors.ShouldContain(new PointsConfigurationValidationError.DuplicateAlias("shared"));
        draft.GamblingCooldownSeconds.ShouldBe(-4);
        draft.GiveawayMinimumPayout.ShouldBe("not-a-number");
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

        failure.ShouldBe(new PointsConfigurationSaveFailure.AliasAlreadyUsed("shared"));
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointsSettings.CountAsync()).ShouldBe(0);
        (await db.CommandAliases.SingleAsync()).Alias.ShouldBe("shared");
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

    private static PointsConfigurationSaveCommand ValidCommand(PointsConfiguration draft)
    {
        return PointsConfigurationValidator
            .Validate(draft)
            .Match(
                command => command,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
            );
    }

    private static PointsConfigurationService CreateService(SqliteBlokeBotDbFactory dbFactory)
    {
        return new(dbFactory, new PointsChangeNotifier(TestEventBus.Create<AppEventKind>()));
    }

    private static async Task<int> SeedHostWithAliasAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string alias
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = host.Id,
                Kind = AppCommandKind.Start,
                Alias = alias,
            }
        );
        await db.SaveChangesAsync();
        return host.Id;
    }
}
