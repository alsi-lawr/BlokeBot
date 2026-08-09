using AngleSharp.Dom;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ManagementPageWorkspaceTests
{
    [Test]
    public async Task RequestBoards_CreateUpdateAndInvalidDraftPreserveOwnedState()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var service = new RequestBoardService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);
        var page = context.Render<RequestBoardsPage>();

        _ = page.WaitForElement("[data-selected-field-editor]");
        (await CountAsync(database, board: true)).ShouldBe(0);

        FindButton(page, "+ Add question").Click();
        page.WaitForAssertion(() => page.FindAll("[data-question-row]").Count.ShouldBe(2));
        page.Find("[data-selected-field-editor] input[id^='request-field-key-']").Input("notes");
        page.FindAll("[data-question-row]")[0].Click();
        page.FindAll("[data-question-row]")[1].Click();
        page.Find("[data-selected-field-editor] input[id^='request-field-key-']")
            .GetAttribute("value")
            .ShouldBe("notes");

        page.Find("#request-board-slug").Input("clips");
        page.Find("#request-board-name").Input("Clip reviews");
        FindButton(page, "Save board").Click();
        _ = page.WaitForElement("a[href='/requests/streamer/clips']");
        (await CountAsync(database, board: true)).ShouldBe(1);

        page.Find("#request-board-name").Input("Updated clip reviews");
        FindButton(page, "Save board").Click();
        await using (var db = await database.CreateDbContextAsync())
        {
            (await db.RequestBoards.SingleAsync()).Title.ShouldBe("Updated clip reviews");
        }

        FindButton(page, "+ New board").Click();
        page.Find("#request-board-name").Input("Unsaved board");
        page.Find("#request-board-submission-limit").Input("not-a-number");
        FindButton(page, "Save board").Click();
        _ = page.WaitForElement("[role='alert']");
        (await CountAsync(database, board: true)).ShouldBe(1);
        page.Find("#request-board-name").GetAttribute("value").ShouldBe("Unsaved board");

        page.FindAll("aside button")
            .Single(button => button.TextContent.Contains("Updated clip reviews"))
            .Click();
        page.Find("#request-board-name").GetAttribute("value").ShouldBe("Updated clip reviews");
    }

    [Test]
    public async Task PlayQueues_CreateZeroFieldQueueAndInvalidDraftPreserveOwnedState()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var service = new PlayQueueService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);
        _ = context.Services.AddSingleton<IPrivateLobbyDelivery>(new NoopPrivateLobbyDelivery());
        var page = context.Render<PlayQueuesPage>();

        _ = page.WaitForElement("[data-question-row]");
        (await CountAsync(database, board: false)).ShouldBe(0);
        while (page.FindAll("[data-question-row]").Count > 0)
        {
            FindButton(page, "Remove question").Click();
        }

        page.Find("#queue-slug").Input("community");
        page.Find("#queue-name").Input("Community games");
        page.Find("#queue-activity").Input("Example game");
        FindButton(page, "Save queue").Click();
        _ = page.WaitForElement("a[href='/queues/streamer/community']");
        (await CountAsync(database, board: false)).ShouldBe(1);
        await using (var db = await database.CreateDbContextAsync())
        {
            (await db.PlayQueues.SingleAsync()).Fields.ShouldBeEmpty();
        }

        page.Find("#queue-name").Input("Updated community games");
        FindButton(page, "Save queue").Click();
        await using (var db = await database.CreateDbContextAsync())
        {
            (await db.PlayQueues.SingleAsync()).Name.ShouldBe("Updated community games");
        }

        FindButton(page, "+ New queue").Click();
        page.Find("#queue-name").Input("Unsaved queue");
        page.Find("#queue-capacity").Input("not-a-number");
        FindButton(page, "Save queue").Click();
        _ = page.WaitForElement("[role='alert']");
        (await CountAsync(database, board: false)).ShouldBe(1);
        page.Find("#queue-name").GetAttribute("value").ShouldBe("Unsaved queue");

        page.FindAll("aside button")
            .Single(button => button.TextContent.Contains("Updated community games"))
            .Click();
        page.Find("#queue-name").GetAttribute("value").ShouldBe("Updated community games");
    }

    private static IElement FindButton<TComponent>(
        IRenderedComponent<TComponent> page,
        string label
    )
        where TComponent : IComponent =>
        page.FindAll("button").Single(button => button.TextContent.Trim() == label);

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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<int> CountAsync(SqliteBlokeBotDbFactory database, bool board)
    {
        await using var db = await database.CreateDbContextAsync();
        return board ? await db.RequestBoards.CountAsync() : await db.PlayQueues.CountAsync();
    }

    private sealed class NoopPrivateLobbyDelivery : IPrivateLobbyDelivery
    {
        public Task<IReadOnlyList<PrivateLobbyDeliveryOutcome>> DeliverAsync(
            string hostLogin,
            string lobbyCode,
            IReadOnlyList<PrivateLobbyRecipient> recipients,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<PrivateLobbyDeliveryOutcome>>([]);
    }
}
