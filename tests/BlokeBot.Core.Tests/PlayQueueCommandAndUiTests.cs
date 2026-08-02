using System.Security.Claims;
using AngleSharp.Dom;
using BlokeBot.Commands;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
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

        await using (var disable = await database.CreateDbContextAsync())
        {
            var persistedHost = await disable.Hosts.SingleAsync();
            persistedHost.EnabledFeatures &= ~HostFeatureFlags.PlayWithViewers;
            await disable.SaveChangesAsync();
        }
        var responseCount = responses.Count;
        await DispatchAsync(
            dispatcher,
            Message("viewer", "!join squad region=eu preferred-role=Tank"),
            responses
        );
        await DispatchAsync(dispatcher, Message("viewer", "!queue squad"), responses);

        responses.Count.ShouldBe(responseCount);
        await using var verifyDisabled = await database.CreateDbContextAsync();
        (await verifyDisabled.PlayQueueEntries.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task PublicPage_AuthenticatedViewerActionsUseOAuthIdentity()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database);
        var service = new PlayQueueService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = await service.ConfigureAsync(
            host,
            Queue("squad") with
            {
                Capacity = 1,
                Fields = [],
                RoleRequirements = [],
            },
            CancellationToken.None
        );
        using var context = new BunitContext();
        context.Services.AddSingleton(service);
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("OAuth Display");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "oauth-user-id"),
            new Claim(ClaimTypes.Name, "OAuth Display"),
            new Claim(AuthClaims.Login, "oauth_viewer")
        );

        var page = RenderPublicPage(context);

        page.WaitForAssertion(() => page.Markup.ShouldContain("Signed in with Twitch as"));
        page.Markup.ShouldContain("OAuth Display");
        page.Markup.ShouldContain("@oauth_viewer");
        page.FindAll("#queue-viewer-login").ShouldBeEmpty();

        await page.Find("button.btn-primary").ClickAsync(new());
        page.WaitForAssertion(() => page.Markup.ShouldContain("You are position 1."));
        await AssertIdentityAsync(
            database,
            "id:oauth-user-id",
            "oauth-user-id",
            "oauth_viewer",
            "OAuth Display",
            PlayQueueEntryStatus.Waiting
        );

        await FindButton(page, "Check position").ClickAsync(new());
        page.WaitForAssertion(() => page.Markup.ShouldContain("You are position 1 (Waiting)."));

        var readyCheck = await service.StartReadyCheckAsync(
            host,
            await EntryIdAsync(database),
            CancellationToken.None
        );
        readyCheck.ShouldBeOfType<PlayQueueResult<ModeratorPlayQueueEntryView>.Succeeded>();
        await FindButton(page, "I'm ready").ClickAsync(new());
        page.WaitForAssertion(() => page.Markup.ShouldContain("You are ready."));
        await AssertIdentityAsync(
            database,
            "id:oauth-user-id",
            "oauth-user-id",
            "oauth_viewer",
            "OAuth Display",
            PlayQueueEntryStatus.Ready
        );

        var selection = await service.SelectPartyAsync(
            host,
            "squad",
            false,
            CancellationToken.None
        );
        selection.ShouldBeOfType<PlayQueueResult<PlayQueueSelection>.Succeeded>();

        await FindButton(page, "Leave").ClickAsync(new());
        page.WaitForAssertion(() => page.Markup.ShouldContain("You left the queue."));
        await AssertIdentityAsync(
            database,
            "id:oauth-user-id",
            "oauth-user-id",
            "oauth_viewer",
            "OAuth Display",
            PlayQueueEntryStatus.Left
        );
    }

    [Test]
    public async Task PublicPage_AnonymousViewerMustSignInAndCannotMutateTheQueue()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database);
        var service = new PlayQueueService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = await service.ConfigureAsync(
            host,
            Queue("squad") with
            {
                Fields = [],
                RoleRequirements = [],
            },
            CancellationToken.None
        );
        using var context = new BunitContext();
        context.Services.AddSingleton(service);

        var page = RenderPublicPage(context);

        page.WaitForAssertion(() => page.Markup.ShouldContain("Sign in with Twitch to join"));
        page.FindAll("#queue-viewer-login").ShouldBeEmpty();
        page.FindAll("button").ShouldAllBe(button => button.HasAttribute("disabled"));
        page.Find("a").GetAttribute("href")!.ShouldContain("/auth/login?start=true");
        await page.Find("button.btn-primary").ClickAsync(new());
        await using var verify = await database.CreateDbContextAsync();
        (await verify.PlayQueueEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task PublicPage_RendersPublicFieldsWithoutPrivateIdentityValues()
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
                new("private_viewer", "secret-twitch-id", "Private Viewer"),
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
        page.Markup.ShouldContain("Entry fields are optional and public");
        page.Markup.ShouldContain("names are hidden");
        page.Markup.ShouldNotContain("private_viewer");
        page.Markup.ShouldNotContain("secret-twitch-id");
        page.Markup.ShouldContain("SECRET-REGION");
        page.Markup.ShouldContain("Tank");
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

    private static ConfigurePlayQueueCommand Queue(string slug) =>
        new(
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
                new("region", "Region"),
                new("preferred-role", "Preferred role", ["Tank", "Healer", "Damage"]),
            ],
            [new("Tank", 1)]
        );

    private static IRenderedComponent<PublicPlayQueuePage> RenderPublicPage(BunitContext context) =>
        context.Render<PublicPlayQueuePage>(parameters =>
            parameters
                .Add(value => value.Channel, "streamer")
                .Add(value => value.QueueSlug, "squad")
        );

    private static IElement FindButton(IRenderedComponent<PublicPlayQueuePage> page, string text) =>
        page.FindAll("button").Single(button => button.TextContent.Trim() == text);

    private static async Task AssertIdentityAsync(
        SqliteBlokeBotDbFactory database,
        string identityKey,
        string? twitchUserId,
        string normalizedLogin,
        string displayName,
        PlayQueueEntryStatus status
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var entry = await db.PlayQueueEntries.SingleAsync();
        entry.IdentityKey.ShouldBe(identityKey);
        entry.TwitchUserId.ShouldBe(twitchUserId);
        entry.NormalizedLogin.ShouldBe(normalizedLogin);
        entry.DisplayName.ShouldBe(displayName);
        entry.Status.ShouldBe(status);
    }

    private static async Task<long> EntryIdAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        return await db.PlayQueueEntries.Select(entry => entry.Id).SingleAsync();
    }

    private static async Task DispatchAsync(
        ChatCommandDispatcher dispatcher,
        ChatMessage message,
        List<string> responses
    ) =>
        await dispatcher.DispatchResponsesAsync(
            message,
            (response, _) =>
            {
                responses.Add(response.Message);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

    private static ChatMessage Message(
        string login,
        string text,
        IReadOnlyDictionary<string, string>? tags = null
    ) => new(login, "streamer", text, string.Empty, tags ?? new Dictionary<string, string>());

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
