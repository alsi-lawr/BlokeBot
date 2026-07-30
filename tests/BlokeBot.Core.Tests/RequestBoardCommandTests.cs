using BlokeBot.Commands;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class RequestBoardCommandTests
{
    [Test]
    public async Task ChatCommands_SubmitListVoteAndEnforceModeratorTransitions()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var boardService = new RequestBoardService(database, events, TimeProvider.System);
        _ = await boardService.ConfigureAsync(
            hostId,
            new ConfigureRequestBoardCommand(
                "games",
                "Game requests",
                "Suggest games.",
                true,
                "0",
                RequestBoardRefundPolicy.Never,
                3,
                0,
                10,
                true,
                [
                    new RequestBoardFieldCommand(
                        "details",
                        "Details",
                        RequestBoardFieldKind.Text,
                        true,
                        500
                    ),
                ]
            ),
            CancellationToken.None
        );
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        services.AddSingleton(boardService);
        services.AddChatCommands().AddCommandModule<RequestBoardCommandModule>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ChatCommandDispatcher>();
        var responses = new List<string>();

        await DispatchAsync(
            dispatcher,
            Message(
                "viewer",
                "!request games A fun game | details=Please play this",
                new Dictionary<string, string> { ["id"] = Guid.NewGuid().ToString() }
            ),
            responses
        );
        await DispatchAsync(dispatcher, Message("viewer", "!requests games"), responses);
        await DispatchAsync(dispatcher, Message("viewer", "!requestapprove 1"), responses);

        responses.ShouldContain(value => value.Contains("submitted for moderator review"));
        responses.ShouldContain(value => value.Contains("/requests/streamer/games"));
        responses[^1].ShouldContain("moderator-only");
        (
            await boardService.GetModeratorSubmissionAsync(hostId, 1, CancellationToken.None)
        )!.Public.Status.ShouldBe(RequestSubmissionStatus.Pending);

        await DispatchAsync(
            dispatcher,
            Message(
                "moderator",
                "!requestapprove 1",
                new Dictionary<string, string> { ["mod"] = "1" }
            ),
            responses
        );
        await DispatchAsync(dispatcher, Message("viewer", "!requestvote 1"), responses);

        responses[^2].ShouldContain("now approved");
        responses[^1].ShouldContain("Vote recorded");
        (
            await boardService.GetModeratorSubmissionAsync(hostId, 1, CancellationToken.None)
        )!.Public.VoteCount.ShouldBe(1);

        await using (var disable = await database.CreateDbContextAsync())
        {
            var host = await disable.Hosts.SingleAsync();
            host.EnabledFeatures &= ~HostFeatureFlags.RequestBoards;
            await disable.SaveChangesAsync();
        }
        var responseCount = responses.Count;
        await DispatchAsync(dispatcher, Message("viewer", "!requests games"), responses);
        await DispatchAsync(
            dispatcher,
            Message(
                "viewer",
                "!request games Suppressed request",
                new Dictionary<string, string> { ["id"] = Guid.NewGuid().ToString() }
            ),
            responses
        );

        responses.Count.ShouldBe(responseCount);
        await using var verifyDisabled = await database.CreateDbContextAsync();
        (await verifyDisabled.RequestSubmissions.CountAsync()).ShouldBe(1);
    }

    private static async Task DispatchAsync(
        ChatCommandDispatcher dispatcher,
        ChatMessage message,
        List<string> responses
    )
    {
        await dispatcher.DispatchResponsesAsync(
            message,
            (response, _) =>
            {
                responses.Add(response.Message);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );
    }

    private static ChatMessage Message(
        string login,
        string text,
        IReadOnlyDictionary<string, string>? tags = null
    )
    {
        return new ChatMessage(
            login,
            "streamer",
            text,
            $":{login}!u@h PRIVMSG #streamer :{text}",
            tags ?? new Dictionary<string, string>()
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
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
