using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BingoCommandTests
{
    [Test]
    public async Task ViewerJoinAndDisabledGate_UseMessageIdentityWithoutSuppressedMutation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "streamer-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Bingo,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var service = new BingoService(
            database,
            new CommunityProgressionService(
                database,
                TestEventBus.Create<AppEventKind>(),
                TimeProvider.System
            ),
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = Success(
            await service.SaveTemplateAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    null,
                    "Viewer join",
                    new(3),
                    Enumerable
                        .Range(1, 9)
                        .Select(value =>
                            (BingoSquareDefinition)
                                new BingoSquareDefinition.Manual(
                                    new($"s-{value}"),
                                    $"Square {value}"
                                )
                        )
                        .ToArray(),
                    false,
                    BingoWinReward.None,
                    BingoWinReward.None,
                    new("streamer-id", "streamer")
                ),
                default
            )
        );
        var template = (await service.GetTemplatesAsync(hostId, default)).Single();
        _ = Success(
            await service.CreateGameAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    template.Id,
                    BingoGameMode.UniquePerViewer,
                    "seed",
                    null,
                    null,
                    [],
                    new("streamer-id", "streamer")
                ),
                default
            )
        );
        var services = new ServiceCollection();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = services.AddSingleton(service);
        _ = services.AddChatCommands().AddCommandModule<BingoCommandModule>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ChatCommandDispatcher>();
        var responses = new List<string>();

        await DispatchAsync(dispatcher, "!bingojoin", responses);
        responses.Single().ShouldContain("joined Bingo");
        await using (var db = await database.CreateDbContextAsync())
        {
            var participant = await db.BingoParticipants.SingleAsync();
            participant.TwitchUserId.ShouldBe("viewer-id");
            participant.Login.ShouldBe("viewer");
            var host = await db.Hosts.SingleAsync();
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }
        responses.Clear();

        await DispatchAsync(dispatcher, "!bingoleave", responses);

        responses.ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.BingoParticipants.SingleAsync()).Login.ShouldBe("viewer");
        (await verify.BingoModerationAudit.CountAsync()).ShouldBe(1);
    }

    private static async Task DispatchAsync(
        ChatCommandDispatcher dispatcher,
        string text,
        List<string> responses
    ) =>
        await dispatcher.DispatchResponsesAsync(
            new ChatMessage(
                "viewer",
                "streamer",
                text,
                "raw",
                new Dictionary<string, string>
                {
                    ["id"] = Guid.NewGuid().ToString(),
                    ["user-id"] = "viewer-id",
                    ["display-name"] = "Viewer Name",
                }
            ),
            (response, _) =>
            {
                responses.Add(response.Message);
                return ValueTask.CompletedTask;
            },
            default
        );

    private static BingoOperationOutcome.Succeeded Success(BingoOperationOutcome result) =>
        result.ShouldBeOfType<BingoOperationOutcome.Succeeded>();
}
