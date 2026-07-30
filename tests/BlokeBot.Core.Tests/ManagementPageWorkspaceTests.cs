using AngleSharp.Dom;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class ManagementPageWorkspaceTests
{
    [Test]
    public async Task RequestBoards_CreateAndFieldWorkspace_KeepDraftStateExplicitAndAccessible()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var service = new RequestBoardService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        context.Services.AddSingleton(service);

        var page = context.Render<RequestBoardsPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("#request-board-configuration").TextContent.ShouldBe("New board — not saved");
            page.Find("[role='status']").TextContent.ShouldContain("Save board to create it");
            FindButton(page, "New board").HasAttribute("disabled").ShouldBeTrue();
            page.FindAll("a[href^='/requests/streamer/']").ShouldBeEmpty();
            page.FindAll("[data-selected-field-editor]").Count.ShouldBe(1);
        });
        AssertEditorAssociation(page);
        (await CountAsync(database, board: true)).ShouldBe(0);

        FindButton(page, "Add field").Click();
        page.WaitForAssertion(() =>
        {
            page.FindAll(".management-field-inventory-row").Count.ShouldBe(2);
            page.FindAll("[data-selected-field-editor]").Count.ShouldBe(1);
        });
        AssertEditorAssociation(page);
        var addedEditor = page.Find("[data-selected-field-editor]");
        addedEditor
            .QuerySelector("input[id^='request-field-key-']")
            .ShouldNotBeNull()
            .Input("notes");
        context.JSInterop.Invocations.ShouldContain(invocation =>
            invocation.Identifier == "Blazor._internal.domWrapper.focus"
        );

        page.FindAll(".management-field-inventory-row")[0].QuerySelector("button")!.Click();
        page.FindAll(".management-field-inventory-row")[1].QuerySelector("button")!.Click();
        page.Find("[data-selected-field-editor] input[id^='request-field-key-']")
            .GetAttribute("value")
            .ShouldBe("notes");
        FindButton(page, "Remove field").Click();
        page.FindAll(".management-field-inventory-row").Count.ShouldBe(1);
        page.FindAll("[data-selected-field-editor]").Count.ShouldBe(1);
        page.Find(".management-field-inventory-row").GetAttribute("data-current").ShouldBe("true");
        FindButton(page, "Remove field").HasAttribute("disabled").ShouldBeTrue();
        page.Markup.ShouldContain("needs at least one field");

        page.Find("#request-board-slug").Input("clips");
        page.Find("#request-board-name").Input("Clip reviews");
        FindButton(page, "Save board").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("[role='status']").TextContent.ShouldContain("Board created.");
            page.Find("#request-board-configuration").TextContent.ShouldBe("Edit board");
            page.Find("a[href='/requests/streamer/clips']").ShouldNotBeNull();
        });
        (await CountAsync(database, board: true)).ShouldBe(1);

        page.Find("#request-board-name").Input("Updated clip reviews");
        FindButton(page, "Save board").Click();
        page.WaitForAssertion(() =>
            page.Find("[role='status']").TextContent.ShouldContain("Board saved.")
        );

        FindButton(page, "New board").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("#request-board-configuration").TextContent.ShouldBe("New board — not saved");
            FindButton(page, "New board").HasAttribute("disabled").ShouldBeTrue();
            page.FindAll("a[href^='/requests/streamer/']").ShouldBeEmpty();
        });
        page.Find("#request-board-name").Input("Unsaved board");
        page.Find("#request-board-submission-limit").Input("not-a-number");
        FindButton(page, "Save board").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("[role='alert']").TextContent.ShouldContain("must contain valid numbers");
            page.Find("#request-board-name").GetAttribute("value").ShouldBe("Unsaved board");
            page.Find("#request-board-configuration").TextContent.ShouldBe("New board — not saved");
        });

        page.Find("#request-board-submission-limit").Input("3");
        FindButton(page, "Save board").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("[role='alert']").TextContent.ShouldNotBeNullOrWhiteSpace();
            page.Find("#request-board-name").GetAttribute("value").ShouldBe("Unsaved board");
            page.Find("#request-board-configuration").TextContent.ShouldBe("New board — not saved");
        });
        (await CountAsync(database, board: true)).ShouldBe(1);
        page.FindAll("aside button")
            .Single(button => button.TextContent.Contains("Updated clip reviews"))
            .Click();
        page.WaitForAssertion(() =>
        {
            page.Find("#request-board-configuration").TextContent.ShouldBe("Edit board");
            FindButton(page, "New board").HasAttribute("disabled").ShouldBeFalse();
            page.Find("#request-board-name").GetAttribute("value").ShouldBe("Updated clip reviews");
        });
    }

    [Test]
    public async Task PlayQueues_CreateAndFieldWorkspace_PreserveValidZeroFieldState()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var service = new PlayQueueService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        context.Services.AddSingleton(service);
        context.Services.AddSingleton<IPrivateLobbyDelivery>(new NoopPrivateLobbyDelivery());

        var page = context.Render<PlayQueuesPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("#queue-config-heading").TextContent.ShouldBe("New queue — not saved");
            page.Find("[role='status']").TextContent.ShouldContain("Save queue to create it");
            FindButton(page, "New queue").HasAttribute("disabled").ShouldBeTrue();
            page.FindAll("a[href^='/queues/streamer/']").ShouldBeEmpty();
            page.FindAll(".management-field-inventory-row").Count.ShouldBe(4);
            page.FindAll("[data-selected-field-editor]").Count.ShouldBe(1);
        });
        AssertEditorAssociation(page);
        (await CountAsync(database, board: false)).ShouldBe(0);

        FindButton(page, "Add field").Click();
        page.WaitForAssertion(() =>
        {
            page.FindAll(".management-field-inventory-row").Count.ShouldBe(5);
            page.FindAll("[data-selected-field-editor]").Count.ShouldBe(1);
        });
        AssertEditorAssociation(page);
        page.Find("[data-selected-field-editor] input[id^='queue-field-key-']").Input("language");
        context.JSInterop.Invocations.ShouldContain(invocation =>
            invocation.Identifier == "Blazor._internal.domWrapper.focus"
        );

        while (page.FindAll(".management-field-inventory-row").Count > 0)
        {
            FindButton(page, "Remove field").Click();
        }
        page.FindAll("[data-selected-field-editor]").ShouldBeEmpty();
        page.Markup.ShouldContain("join without additional details");

        page.Find("#queue-slug").Input("community");
        page.Find("#queue-name").Input("Community games");
        page.Find("#queue-activity").Input("Example game");
        FindButton(page, "Save queue").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("[role='status']").TextContent.ShouldContain("Queue created.");
            page.Find("#queue-config-heading").TextContent.ShouldBe("Edit queue");
            page.Find("a[href='/queues/streamer/community']").ShouldNotBeNull();
            page.FindAll("[data-selected-field-editor]").ShouldBeEmpty();
        });
        (await CountAsync(database, board: false)).ShouldBe(1);

        page.Find("#queue-name").Input("Updated community games");
        FindButton(page, "Save queue").Click();
        page.WaitForAssertion(() =>
            page.Find("[role='status']").TextContent.ShouldContain("Queue saved.")
        );

        FindButton(page, "New queue").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("#queue-config-heading").TextContent.ShouldBe("New queue — not saved");
            FindButton(page, "New queue").HasAttribute("disabled").ShouldBeTrue();
            page.FindAll("a[href^='/queues/streamer/']").ShouldBeEmpty();
        });
        page.Find("#queue-name").Input("Unsaved queue");
        page.Find("#queue-capacity").Input("not-a-number");
        FindButton(page, "Save queue").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("[role='alert']").TextContent.ShouldContain("must contain valid numbers");
            page.Find("#queue-name").GetAttribute("value").ShouldBe("Unsaved queue");
            page.Find("#queue-config-heading").TextContent.ShouldBe("New queue — not saved");
        });

        page.Find("#queue-capacity").Input("4");
        FindButton(page, "Save queue").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("[role='alert']").TextContent.ShouldNotBeNullOrWhiteSpace();
            page.Find("#queue-name").GetAttribute("value").ShouldBe("Unsaved queue");
            page.Find("#queue-config-heading").TextContent.ShouldBe("New queue — not saved");
        });
        (await CountAsync(database, board: false)).ShouldBe(1);
        page.FindAll("aside button")
            .Single(button => button.TextContent.Contains("Updated community games"))
            .Click();
        page.WaitForAssertion(() =>
        {
            page.Find("#queue-config-heading").TextContent.ShouldBe("Edit queue");
            FindButton(page, "New queue").HasAttribute("disabled").ShouldBeFalse();
            page.Find("#queue-name").GetAttribute("value").ShouldBe("Updated community games");
        });
    }

    [Test]
    public async Task FieldLimits_DisableAddAndExplainTheTwelveFieldMaximum()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);

        await using (var boardContext = UiTestContextFactory.Create(database, hostId))
        {
            boardContext.Services.AddSingleton(
                new RequestBoardService(
                    database,
                    TestEventBus.Create<AppEventKind>(),
                    TimeProvider.System
                )
            );
            var page = boardContext.Render<RequestBoardsPage>();
            page.WaitForAssertion(() =>
                page.FindAll(".management-field-inventory-row").Count.ShouldBe(1)
            );
            for (var index = 1; index < RequestBoardLimits.MaximumFields; index++)
            {
                FindButton(page, "Add field").Click();
            }

            FindButton(page, "Add field").HasAttribute("disabled").ShouldBeTrue();
            page.Find("#request-field-limit")
                .TextContent.ShouldContain("Maximum of 12 fields reached");
        }

        await using (var queueContext = UiTestContextFactory.Create(database, hostId))
        {
            queueContext.Services.AddSingleton(
                new PlayQueueService(
                    database,
                    TestEventBus.Create<AppEventKind>(),
                    TimeProvider.System
                )
            );
            queueContext.Services.AddSingleton<IPrivateLobbyDelivery>(
                new NoopPrivateLobbyDelivery()
            );
            var page = queueContext.Render<PlayQueuesPage>();
            page.WaitForAssertion(() =>
                page.FindAll(".management-field-inventory-row").Count.ShouldBe(4)
            );
            for (var index = 4; index < PlayQueueLimits.MaximumFields; index++)
            {
                FindButton(page, "Add field").Click();
            }

            FindButton(page, "Add field").HasAttribute("disabled").ShouldBeTrue();
            page.Find("#queue-field-limit")
                .TextContent.ShouldContain("Maximum of 12 fields reached");
        }
    }

    private static IElement FindButton<TComponent>(
        IRenderedComponent<TComponent> page,
        string label
    )
        where TComponent : IComponent
    {
        return page.FindAll("button").Single(button => button.TextContent.Trim() == label);
    }

    private static void AssertEditorAssociation<TComponent>(IRenderedComponent<TComponent> page)
        where TComponent : IComponent
    {
        var row = page.Find(".management-field-inventory-row[data-current='true']");
        var editor = page.Find("[data-selected-field-editor]");
        var label = row.QuerySelector("p[id]");
        label.ShouldNotBeNull();
        editor.GetAttribute("aria-labelledby").ShouldBe(label!.Id);
        editor.Id.ShouldNotBeNullOrWhiteSpace();
        var editButton = row.QuerySelector("button");
        editButton.ShouldNotBeNull();
        editButton!.GetAttribute("aria-pressed").ShouldBe("true");
        editButton.GetAttribute("aria-controls").ShouldBe(editor.Id);
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
        )
        {
            return Task.FromResult<IReadOnlyList<PrivateLobbyDeliveryOutcome>>([]);
        }
    }
}
