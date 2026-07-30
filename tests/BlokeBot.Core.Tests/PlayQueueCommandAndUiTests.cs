using BlokeBot.Commands;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PlayQueueCommandAndUiTests
{
    [Test]
    public async Task ChatCommands_RequireSlugForMultipleQueuesAndSupportViewerLifecycle()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database);
        var service = new PlayQueueService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = await service.ConfigureAsync(host, Queue("squad"), CancellationToken.None);
        _ = await service.ConfigureAsync(
            host,
            Queue("duos") with
            {
                Capacity = 2,
                RoleRequirements = [],
            },
            CancellationToken.None
        );
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        services.AddSingleton(service);
        services.AddChatCommands().AddCommandModule<PlayQueueCommandModule>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ChatCommandDispatcher>();
        var responses = new List<string>();

        await DispatchAsync(dispatcher, Message("viewer", "!join"), responses);
        await DispatchAsync(
            dispatcher,
            Message(
                "viewer",
                "!join squad region=eu preferred-role=Tank",
                new Dictionary<string, string> { ["user-id"] = "42" }
            ),
            responses
        );
        await DispatchAsync(
            dispatcher,
            Message(
                "viewer",
                "!position squad",
                new Dictionary<string, string> { ["user-id"] = "42" }
            ),
            responses
        );
        await DispatchAsync(dispatcher, Message("viewer", "!queueclose squad"), responses);
        await DispatchAsync(
            dispatcher,
            Message(
                "moderator",
                "!queueclose squad",
                new Dictionary<string, string> { ["mod"] = "1" }
            ),
            responses
        );

        responses[0].ShouldContain("Choose a queue");
        responses[1].ShouldContain("You joined");
        responses[2].ShouldContain("position 1");
        responses[3].ShouldContain("moderator-only");
        responses[4].ShouldContain("now closed");
    }

    [Test]
    public async Task PublicPage_RendersPrivacyRuleAndNoPrivateEntryValues()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database);
        var service = new PlayQueueService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = await service.ConfigureAsync(host, Queue("squad"), CancellationToken.None);
        _ = await service.JoinAsync(
            host,
            "squad",
            new JoinPlayQueueCommand(
                new("private_viewer"),
                0,
                new Dictionary<string, string>
                {
                    ["region"] = "SECRET-REGION",
                    ["preferred-role"] = "Tank",
                }
            ),
            CancellationToken.None
        );
        using var context = new BunitContext();
        context.Services.AddSingleton(service);

        var page = context.Render<PublicPlayQueuePage>(parameters =>
            parameters
                .Add(value => value.Channel, "streamer")
                .Add(value => value.QueueSlug, "squad")
        );
        page.WaitForAssertion(() => page.Find("h1").TextContent.ShouldBe("Community squad"));
        page.Markup.ShouldContain("visible only to moderators");
        page.Markup.ShouldContain("names are hidden");
        page.Markup.ShouldNotContain("private_viewer");
        page.Markup.ShouldNotContain("SECRET-REGION");
    }

    [Test]
    public void ModeratorAndPublicRoutes_DeclareCorrectAudiences()
    {
        typeof(PlayQueuesPage)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ShouldHaveSingleItem()
            .Policy.ShouldBe("HostSelected");
        typeof(PublicPlayQueuePage)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
            .ShouldHaveSingleItem();
    }

    private static ConfigurePlayQueueCommand Queue(string slug)
    {
        return new(
            slug,
            "Community squad",
            "Example game",
            4,
            true,
            PlayQueueSelectionMode.LeastRecentParticipation,
            false,
            120,
            30,
            15,
            [
                new("region", "Region", true),
                new("preferred-role", "Preferred role", true, ["Tank", "Healer", "Damage"]),
            ],
            [new("Tank", 1)]
        );
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
        return new(login, "streamer", text, string.Empty, tags ?? new Dictionary<string, string>());
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
