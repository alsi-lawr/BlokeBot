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
    public async Task ValidatedCommand_MutatingDraftBeforeExecution_PersistsItsRepresentativeSnapshot()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var service = CreateService(dbFactory);
        var draft = new PointsConfiguration { PointLabel = " channel points " };
        draft.Aliases.PointsAliases = " Score ";
        draft.Replies.BalanceReply = " Balance: {balance}. ";
        draft.ReplyDelivery.DeliverAsWhisper(PointsReplyKeys.Balance);
        var command = ValidCommand(draft);

        draft.PointLabel = "mutated";
        draft.Aliases.PointsAliases = "mutated";
        draft.Replies.BalanceReply = "mutated";
        draft.ReplyDelivery.DeliverInChat(PointsReplyKeys.Balance);

        _ = await service.SaveConfiguration(hostId, command).ExecuteAsync(CancellationToken.None);
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);

        loaded.PointLabel.ShouldBe("channel points");
        loaded.Aliases.PointsAliases.ShouldBe("score");
        loaded.Replies.BalanceReply.ShouldBe("Balance: {balance}.");
        loaded.ReplyDelivery.IsWhisper(PointsReplyKeys.Balance).ShouldBeTrue();
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
        var failure = result.Match<PointsConfigurationSaveFailure?>(
            static _ => null,
            static error => error
        );

        _ = failure.ShouldNotBeNull();
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
        var failure = result.Match<PointsConfigurationSaveFailure?>(
            static _ => null,
            static error => error
        );

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

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            service.SaveConfiguration(1, command).ExecuteAsync(cancellation.Token).AsTask()
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointsSettings.CountAsync()).ShouldBe(0);
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
        _ = db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = hostId,
                Kind = AppCommandKind.Start,
                Alias = alias,
            }
        );
        _ = await db.SaveChangesAsync();
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
        _ = db.CustomCommands.Add(
            new CustomCommand
            {
                HostId = hostId,
                Name = "Existing custom command",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Aliases = [new CustomCommandAlias { HostId = hostId, Alias = alias }],
            }
        );
        _ = await db.SaveChangesAsync();
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
