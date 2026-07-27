using BlokeBot.Announcements;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandSettingsUiTests
{
    [Test]
    public async Task ActionKind_ChangingToMessage_HidesCounterControl()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);

        var cut = context.Render<CustomCommandSettingsPage>();

        var actionSelect = cut.Find($"#command-{seeded.CommandId}-action-kind");
        var reply = cut.Find($"#command-{seeded.CommandId}-0-argument-reply");
        reply.GetAttribute("aria-invalid").ShouldBeNull();
        reply.GetAttribute("aria-describedby").ShouldBeNull();
        actionSelect.Change(CustomCommandActionKind.Message.ToString());

        cut.FindAll($"#command-{seeded.CommandId}-counter-id").ShouldBeEmpty();
    }

    [Test]
    public async Task NewAnnouncement_Adding_SavesWithoutValidation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find("button[aria-controls='custom-announcement-settings']").Click();

        cut.Find("button[data-action='add-scheduled-message']").Click();

        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.FindAll("[data-validation-summary]").ShouldBeEmpty();
        toasts.Current.ShouldHaveSingleItem().Kind.ShouldBe(ToastKind.Success);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomAnnouncements.CountAsync()).ShouldBe(2);
    }

    [Test]
    [Arguments(ValidationSection.Replies)]
    [Arguments(ValidationSection.Commands)]
    [Arguments(ValidationSection.Counters)]
    public async Task CollapsedValidationSection_SavingInvalidEntity_ReopensAndFocusesTarget(
        ValidationSection section
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();
        var expected = InvalidateSection(cut, seeded, section);
        var disclosure = cut.Find($"button[aria-controls='{expected.ContentId}']");

        disclosure.Click();
        cut.Find($"#{expected.ContentId}").HasAttribute("hidden").ShouldBeTrue();
        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.Find($"#{expected.ContentId}").ShouldNotBeNull();
        var control = cut.Find($"#{expected.ControlId}");
        control.GetAttribute("aria-invalid").ShouldBe("true");
        control.GetAttribute("aria-describedby").ShouldBe($"{expected.ControlId}-error");
        var focus = context.JSInterop.Invocations.Last(invocation =>
            invocation.Identifier == "Blazor._internal.domWrapper.focus"
        );
        focus.Arguments[0].ShouldBeElementReferenceTo(control);
    }

    [Test]
    public async Task InvalidCustomCommandEditor_Saving_ShowsAllErrorsUntilCorrected()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find("#custom-command-message-library-tab").Click();
        cut.Find("button[data-action='edit-reply']").Click();
        cut.Find("textarea").Input(string.Empty);
        cut.Find("#custom-command-commands-tab").Click();
        cut.Find("button[aria-controls='custom-announcement-settings']").Click();
        cut.Find("button[data-action='edit-scheduled-message']").Click();
        cut.Find("button[aria-controls='custom-announcement-delivery-details']").Click();
        cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay").Change("0");
        cut.Find($"#announcement-{seeded.AnnouncementId}-occurrence-lifetime").Change("61");

        cut.Find("button[aria-label='Save custom commands']").Click();

        ValidationMessages(cut).Length.ShouldBe(3);
        cut.Find("#custom-command-message-library-tab")
            .GetAttribute("aria-selected")
            .ShouldBe("true");
        var invalidMessage = cut.Find("textarea");
        invalidMessage.GetAttribute("aria-invalid").ShouldBe("true");
        invalidMessage.GetAttribute("aria-describedby").ShouldNotBeNull();
        cut.Find("#custom-command-commands-tab").Click();
        cut.Find("button[data-action='edit-scheduled-message']").Click();
        cut.Find("button[aria-controls='custom-announcement-delivery-details']").Click();
        var invalidRetry = cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay");
        invalidRetry.GetAttribute("aria-invalid").ShouldBe("true");
        invalidRetry
            .GetAttribute("aria-describedby")
            .ShouldBe($"announcement-{seeded.AnnouncementId}-retry-delay-error");
        toasts.Current.ShouldHaveSingleItem().Kind.ShouldBe(ToastKind.Error);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (await db.CustomMessageLibraryEntries.FindAsync(seeded.MessageEntryId))!.Name.ShouldBe(
                "Message"
            );
        }

        cut.Find("#custom-command-message-library-tab").Click();
        cut.Find("button[data-action='edit-reply']").Click();
        cut.Find("textarea").Input("Corrected reply");
        cut.Find("button[aria-label='Save custom commands']").Click();

        ValidationMessages(cut).Length.ShouldBe(2);

        cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay").Change("2");
        cut.Find($"#announcement-{seeded.AnnouncementId}-occurrence-lifetime").Change("30");
        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.FindAll("[data-validation-summary]").ShouldBeEmpty();
        var success = toasts.Current.Last();
        success.Kind.ShouldBe(ToastKind.Success);
        await using var savedDb = await dbFactory.CreateDbContextAsync();
        (
            await savedDb
                .CustomMessageLibraryEntries.Include(entry => entry.Variants)
                .SingleAsync(entry => entry.Id == seeded.MessageEntryId)
        )
            .Variants.Single()
            .Text.ShouldBe("Corrected reply");
    }

    [Test]
    public async Task BuiltInAliasCollision_Saving_ActivatesCommandsAndAssociatesTheMatchingAlias()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = seeded.HostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            await db.SaveChangesAsync();
        }

        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find($"#command-{seeded.CommandId}-aliases").Input("points");
        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.Find("#custom-command-commands-tab").GetAttribute("aria-selected").ShouldBe("true");
        var aliases = cut.Find($"#command-{seeded.CommandId}-aliases");
        aliases.GetAttribute("aria-invalid").ShouldBe("true");
        aliases.GetAttribute("aria-describedby").ShouldNotBeNull();
        ValidationMessages(cut).Length.ShouldBe(1);
        toasts.Current.ShouldHaveSingleItem().Kind.ShouldBe(ToastKind.Error);
        await using var savedDb = await dbFactory.CreateDbContextAsync();
        (await savedDb.CustomCommandAliases.SingleAsync()).Alias.ShouldBe("command");
    }

    [Test]
    public async Task RememberedMessageLibraryItem_ReopensOnlyThatEditor()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var module = context.JSInterop.SetupModule("./Components/CollapsibleSection.razor.js");
        module
            .Setup<string?>("readString", "blokebot.task.custom-command-settings")
            .SetResult(
                $"v2:MessageLibrary:Command,{seeded.CommandId}:Reply,{seeded.MessageEntryId}"
            );
        module
            .SetupVoid(
                "writeString",
                "blokebot.task.custom-command-settings",
                $"v2:MessageLibrary:Command,{seeded.CommandId}:Reply,{seeded.MessageEntryId}"
            )
            .SetVoidResult();

        var cut = context.Render<CustomCommandSettingsPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("#custom-command-message-library-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            cut.Find($"#message-entry-{seeded.MessageEntryId}-name").ShouldNotBeNull();
            cut.FindAll($"#command-{seeded.CommandId}-name").ShouldBeEmpty();
        });

        await using var pendingContext = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var pendingModule = pendingContext.JSInterop.SetupModule(
            "./Components/CollapsibleSection.razor.js"
        );
        var pendingRead = pendingModule.Setup<string?>(
            "readString",
            "blokebot.task.custom-command-settings"
        );
        pendingModule
            .SetupVoid(
                "writeString",
                "blokebot.task.custom-command-settings",
                $"v2:MessageLibrary:Command,{seeded.CommandId}:Reply,{seeded.MessageEntryId}"
            )
            .SetVoidResult();
        var pendingCut = pendingContext.Render<CustomCommandSettingsPage>();
        pendingCut.Find("#custom-command-message-library-tab").Click();
        pendingRead.SetResult(
            $"v2:Commands:Command,{seeded.CommandId}:Reply,{seeded.MessageEntryId}"
        );
        pendingCut.WaitForAssertion(() =>
        {
            pendingCut
                .Find("#custom-command-message-library-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            pendingCut.Find($"#message-entry-{seeded.MessageEntryId}-name").ShouldNotBeNull();
        });

        await using var staleContext = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var staleModule = staleContext.JSInterop.SetupModule(
            "./Components/CollapsibleSection.razor.js"
        );
        staleModule
            .Setup<string?>("readString", "blokebot.task.custom-command-settings")
            .SetResult(
                $"v2:MessageLibrary:Reply,{seeded.MessageEntryId}:Reply,{seeded.MessageEntryId}"
            );
        var staleCut = staleContext.Render<CustomCommandSettingsPage>();
        staleCut.WaitForAssertion(() =>
        {
            staleCut
                .Find("#custom-command-commands-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            staleCut.Find($"#command-{seeded.CommandId}-name").ShouldNotBeNull();
        });

        await using var undefinedContext = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var undefinedModule = undefinedContext.JSInterop.SetupModule(
            "./Components/CollapsibleSection.razor.js"
        );
        undefinedModule
            .Setup<string?>("readString", "blokebot.task.custom-command-settings")
            .SetResult($"v2:99:99,{seeded.CommandId}:Reply,{seeded.MessageEntryId}");
        var undefinedCut = undefinedContext.Render<CustomCommandSettingsPage>();
        undefinedCut.WaitForAssertion(() =>
        {
            undefinedCut
                .Find("#custom-command-commands-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            undefinedCut.Find($"#command-{seeded.CommandId}-name").ShouldNotBeNull();
        });
    }

    [Test]
    public async Task InventoryEditing_ReplacesTheOpenEditorAndPreservesUnsavedValues()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find($"#command-{seeded.CommandId}-name").Input("Unsaved command");
        cut.Find("button[data-action='edit-counter']").Click();

        cut.FindAll("input[id^='command-'][id$='-name']").ShouldBeEmpty();
        cut.Find($"#counter-{seeded.CounterId}-name").ShouldNotBeNull();
        cut.Find("button[data-action='edit-command']").Click();
        cut.Find($"#command-{seeded.CommandId}-name")
            .GetAttribute("value")
            .ShouldBe("Unsaved command");
    }

    [Test]
    public async Task EmptyCommands_CreateReply_AddsSelectsAndFocusesTheReplyEditor()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedEmptyConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("button[data-action='create-reply']").Click();

        cut.Find("button[data-action='edit-reply']").ShouldNotBeNull();
        var name = cut.Find("input[id^='message-entry-'][id$='-name']");
        name.GetAttribute("value").ShouldBe("New reply");
        context.JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(name);
        cut.Find("button[aria-label='Save custom commands']").Click();
        cut.Find("input[id^='message-entry-'][id$='-name']").ShouldNotBeNull();
        cut.FindAll("input[id^='message-entry--'][id$='-name']").ShouldBeEmpty();
    }

    [Test]
    public async Task SettingsTabs_ArrowHomeAndEndKeys_SelectAndFocusTheExpectedTab()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("#custom-command-commands-tab")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.Find("#custom-command-message-library-tab")
            .GetAttribute("aria-selected")
            .ShouldBe("true");
        cut.Find("#custom-command-commands-panel").GetAttribute("hidden").ShouldNotBeNull();
        cut.Find("#custom-command-message-library-panel")
            .GetAttribute("aria-labelledby")
            .ShouldBe("custom-command-message-library-tab");
        cut.Find("#custom-command-message-library-panel").GetAttribute("hidden").ShouldBeNull();
        cut.Find("input[id^='message-entry-'][id$='-name']").ShouldNotBeNull();
        cut.FindAll("input[id^='command-'][id$='-name']").ShouldBeEmpty();

        cut.Find("#custom-command-message-library-tab")
            .KeyDown(new KeyboardEventArgs { Key = "Home" });

        cut.Find("#custom-command-commands-tab").GetAttribute("aria-selected").ShouldBe("true");
        cut.Find("#custom-command-message-library-panel").GetAttribute("hidden").ShouldNotBeNull();
        cut.Find("input[id^='command-'][id$='-name']").ShouldNotBeNull();
        cut.FindAll("input[id^='message-entry-'][id$='-name']").ShouldBeEmpty();
        cut.Find("#custom-command-commands-tab").KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.Find("#custom-command-message-library-tab").GetAttribute("tabindex").ShouldBe("0");
    }

    [Test]
    public async Task CommandsWithoutReplies_OffersDirectMessageLibraryAction()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedEmptyConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("button[data-action='create-reply']").Click();

        cut.Find("#custom-command-message-library-tab")
            .GetAttribute("aria-selected")
            .ShouldBe("true");
        cut.Find("#custom-command-message-library-panel").ShouldNotBeNull();
    }

    [Test]
    public async Task InvocationLimit_EditingAndResettingAsSelectedHostManager_RoundTripsAndAudits()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CustomCommandInvocationClaims.Add(
                new CustomCommandInvocationClaim
                {
                    HostId = seeded.HostId,
                    CustomCommandId = seeded.CommandId,
                    TwitchUserId = "viewer-id",
                    ClaimedAtUtc = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("button[aria-controls='custom-command-advanced-settings']").Click();
        cut.Find($"#command-{seeded.CommandId}-invocation-limit")
            .Change(CustomCommandInvocationLimit.OncePerUser.ToString());
        cut.Find("button[aria-label='Save custom commands']").Click();

        await using var savedDb = await dbFactory.CreateDbContextAsync();
        (
            await savedDb
                .CustomCommands.Where(command => command.Id == seeded.CommandId)
                .Select(command => command.InvocationLimit)
                .SingleAsync()
        ).ShouldBe(CustomCommandInvocationLimit.OncePerUser);
        cut.Find("button[aria-controls='custom-command-advanced-settings']").Click();
        cut.FindAll("button[data-action='reset-viewer-use']").Count.ShouldBe(1);
        cut.FindAll("button[data-action='reset-all-viewer-uses']").Count.ShouldBe(1);

        cut.Find("button[data-action='reset-all-viewer-uses']").Click();
        await using (var unchanged = await dbFactory.CreateDbContextAsync())
        {
            (await unchanged.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
            (await unchanged.CustomCommandInvocationResetAudits.CountAsync()).ShouldBe(0);
        }
        cut.Find("button[data-action='confirm-reset-all-viewer-uses']").Click();

        await using var reset = await dbFactory.CreateDbContextAsync();
        (await reset.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
        var audit = await reset.CustomCommandInvocationResetAudits.SingleAsync();
        audit.ActorTwitchUserId.ShouldBe("streamer-id");
        audit.ActorLogin.ShouldBe("streamer");
        audit.Scope.ShouldBe(CustomCommandInvocationResetScope.AllViewers);
        audit.AffectedClaimCount.ShouldBe(1);
    }

    private static string[] ValidationMessages(IRenderedComponent<CustomCommandSettingsPage> page)
    {
        var summary = page.Find("[data-validation-summary]");
        var title = summary.QuerySelector("#custom-command-validation-title");
        title.ShouldNotBeNull();
        title.TextContent.Trim().ShouldBe("Check these settings");
        return summary.QuerySelectorAll("li").Select(item => item.TextContent.Trim()).ToArray();
    }

    private static ValidationSectionExpectation InvalidateSection(
        IRenderedComponent<CustomCommandSettingsPage> page,
        SeededConfiguration seeded,
        ValidationSection section
    )
    {
        return section switch
        {
            ValidationSection.Replies => InvalidateReply(page, seeded),
            ValidationSection.Commands => InvalidateCommand(page, seeded),
            ValidationSection.Counters => InvalidateCounter(page, seeded),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };
    }

    private static ValidationSectionExpectation InvalidateReply(
        IRenderedComponent<CustomCommandSettingsPage> page,
        SeededConfiguration seeded
    )
    {
        page.Find("#custom-command-message-library-tab").Click();
        page.Find("button[data-action='edit-reply']").Click();
        var controlId = $"message-entry-{seeded.MessageEntryId}-name";
        page.Find($"#{controlId}").Input(string.Empty);
        return new("custom-command-replies-settings", controlId);
    }

    private static ValidationSectionExpectation InvalidateCommand(
        IRenderedComponent<CustomCommandSettingsPage> page,
        SeededConfiguration seeded
    )
    {
        var controlId = $"command-{seeded.CommandId}-name";
        page.Find($"#{controlId}").Input(string.Empty);
        return new("custom-command-chat-commands-settings", controlId);
    }

    private static ValidationSectionExpectation InvalidateCounter(
        IRenderedComponent<CustomCommandSettingsPage> page,
        SeededConfiguration seeded
    )
    {
        page.Find("button[data-action='edit-counter']").Click();
        var controlId = $"counter-{seeded.CounterId}-name";
        page.Find($"#{controlId}").Input(string.Empty);
        return new("custom-command-counters-settings", controlId);
    }

    private static async Task<SeededConfiguration> SeedConfigurationAsync(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var entry = new CustomMessageLibraryEntry
        {
            HostId = host.Id,
            Name = "Message",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Variants = [new CustomMessageVariant { SortOrder = 0, Text = "Message text" }],
        };
        var counter = new CustomCounter
        {
            HostId = host.Id,
            Name = "Count",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.AddRange(entry, counter);
        await db.SaveChangesAsync();
        var command = new CustomCommand
        {
            HostId = host.Id,
            Name = "Command",
            Action = new CounterCustomCommandAction
            {
                HostId = host.Id,
                ZeroArgumentMessageLibraryEntryId = entry.Id,
                CounterId = counter.Id,
            },
            Aliases = [new CustomCommandAlias { HostId = host.Id, Alias = "command" }],
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        var announcement = new CustomAnnouncement
        {
            HostId = host.Id,
            Name = "Announcement",
            MessageLibraryEntryId = entry.Id,
            DeliveryPolicy = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
            {
                HostId = host.Id,
                RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(2)),
                OccurrenceLifetime = new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(30)),
            },
            Schedule = new IntervalAfterChatCustomAnnouncementSchedule
            {
                HostId = host.Id,
                IntervalMinutes = 30,
                RequiredChatMessages = 3,
            },
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.AddRange(command, announcement);
        await db.SaveChangesAsync();
        return new SeededConfiguration(host.Id, entry.Id, command.Id, counter.Id, announcement.Id);
    }

    private static async Task<int> SeedEmptyConfigurationAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed record SeededConfiguration(
        int HostId,
        int MessageEntryId,
        int CommandId,
        int CounterId,
        int AnnouncementId
    );

    public enum ValidationSection
    {
        Replies,
        Commands,
        Counters,
    }

    private sealed record ValidationSectionExpectation(string ContentId, string ControlId);
}
