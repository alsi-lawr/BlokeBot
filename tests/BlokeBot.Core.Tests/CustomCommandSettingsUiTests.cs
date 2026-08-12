using BlokeBot.Announcements;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandSettingsUiTests
{
    [Test]
    public async Task MessageLibraryTab_Selecting_OpensSavedRepliesAndUpdatesTheFragment()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("#custom-command-message-library-tab").Click();

        cut.Find(".studio").GetAttribute("data-active-fragment").ShouldBe("message-library");
        _ = cut.Find("[data-selected-editor='reply']").ShouldNotBeNull();
        context
            .Services.GetRequiredService<NavigationManager>()
            .Uri.ShouldEndWith("#message-library");
    }

    [Test]
    public async Task InitialLoadFailure_ShowsDurableRetryAndRecovers()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        _ = context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(
            new FailOnceDbContextFactory(dbFactory)
        );

        var cut = context.Render<CustomCommandSettingsPage>();

        _ = cut.Find("[data-page-state='failure'][role='alert']").ShouldNotBeNull();
        cut.Find("[data-page-state='failure'] button").Click();

        _ = cut.Find($"#command-{seeded.CommandId}-name").ShouldNotBeNull();
        cut.FindAll("[data-page-state='failure']").ShouldBeEmpty();
    }

    [Test]
    public async Task ActionKind_ChangingToMessage_HidesCounterControl()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);

        var cut = context.Render<CustomCommandSettingsPage>();

        var reply = cut.Find($"#command-{seeded.CommandId}-0-argument-reply");
        reply.GetAttribute("aria-invalid").ShouldBeNull();
        reply.GetAttribute("aria-describedby").ShouldBeNull();
        cut.Find("[data-action-kind='Counter']").GetAttribute("aria-pressed").ShouldBe("true");
        cut.Find("[data-action-kind='Message']").Click();

        cut.Find("[data-action-kind='Message']").GetAttribute("aria-pressed").ShouldBe("true");
        cut.FindAll($"#command-{seeded.CommandId}-counter-id").ShouldBeEmpty();
    }

    [Test]
    public async Task RestrictedAccess_Editing_ResolvesStableUsersAndRetainsDraftsAcrossFailuresAndTabs()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        _ = context.Services.AddSingleton<ICustomCommandViewerResolver>(
            new QueueViewerResolver(
                new CustomCommandViewerResolution.Found(new("selected-id", "viewer", "Viewer")),
                new CustomCommandViewerResolution.Found(
                    new("selected-id", "renamed", "Viewer renamed")
                ),
                new CustomCommandViewerResolution.Unavailable()
            )
        );
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find($"#command-{seeded.CommandId}-access-restricted").Click();

        cut.FindAll("[data-command-access] input[type='checkbox']").ShouldBeEmpty();
        var restricted = cut.Find($"#command-{seeded.CommandId}-access-restricted");
        restricted.GetAttribute("aria-pressed").ShouldBe("true");
        cut.Find($"#command-{seeded.CommandId}-access-everyone")
            .GetAttribute("aria-pressed")
            .ShouldBe("false");
        _ = cut.Find("[data-streamer-only]");
        var login = cut.Find($"#command-{seeded.CommandId}-allowed-user");
        login.Input("#");
        cut.Find("button[data-action='add-allowed-user']").Click();
        _ = cut.Find("[data-allowed-user-feedback]");

        login.Input("viewer");
        cut.Find("button[data-action='add-allowed-user']").Click();

        cut.Find("[data-allowed-user-id='selected-id']").TextContent.ShouldContain("@viewer");
        login.GetAttribute("value").ShouldBe(string.Empty);
        SelectTab(cut, "message-library");
        cut.Find("button[data-action='edit-reply']").Click();
        SelectTab(cut, "commands");
        cut.Find("button[data-action='edit-command']").Click();
        cut.Find("[data-allowed-user-id='selected-id']").TextContent.ShouldContain("Viewer");

        login = cut.Find($"#command-{seeded.CommandId}-allowed-user");
        login.Input("renamed");
        cut.Find("button[data-action='add-allowed-user']").Click();

        _ = cut.Find("[data-allowed-user-feedback]");
        login.GetAttribute("value").ShouldBe("renamed");
        cut.FindAll("[data-allowed-user-id='selected-id']").Count.ShouldBe(1);
        cut.Find("button[aria-label='Save custom commands']").Click();

        await using (var saved = await dbFactory.CreateDbContextAsync())
        {
            var user = await saved.CustomCommandAllowedUsers.SingleAsync();
            user.TwitchUserId.ShouldBe("selected-id");
            user.Login.ShouldBe("viewer");
            user.DisplayName.ShouldBe("Viewer");
        }

        login = cut.Find($"#command-{seeded.CommandId}-allowed-user");
        login.Input("offline_viewer");
        cut.Find("button[data-action='add-allowed-user']").Click();

        _ = cut.Find("[data-allowed-user-feedback]");
        login.GetAttribute("value").ShouldBe("offline_viewer");
        cut.Find("button[data-action='remove-allowed-user']").Click();
        cut.FindAll("[data-allowed-user-id='selected-id']").ShouldBeEmpty();
        cut.Find("button[aria-label='Save custom commands']").Click();
        await using var removed = await dbFactory.CreateDbContextAsync();
        (await removed.CustomCommandAllowedUsers.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task SaveFailure_CompletingCallback_ReportsInlineAndRetainsTheAccessDraft()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        var faultingFactory = new ArmableDbContextFactory(dbFactory);
        var logger = new RecordingLogger<UiFaultTelemetry>();
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        _ = context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(faultingFactory);
        _ = context.Services.AddSingleton(new UiFaultTelemetry(logger));
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find($"#command-{seeded.CommandId}-name").Input("Unsaved command");
        cut.Find($"#command-{seeded.CommandId}-access-restricted").Click();
        cut.Find($"#command-{seeded.CommandId}-access-moderators").Click();
        cut.Find($"#command-{seeded.CommandId}-allowed-user").Input("pending_viewer");
        faultingFactory.Fault = new IOException("Expected test save failure.");

        cut.Find("button[aria-label='Save custom commands']").Click();

        _ = cut.Find("[data-allowed-user-feedback]");
        cut.Find($"#command-{seeded.CommandId}-name")
            .GetAttribute("value")
            .ShouldBe("Unsaved command");
        cut.Find($"#command-{seeded.CommandId}-allowed-user")
            .GetAttribute("value")
            .ShouldBe("pending_viewer");
        cut.Find($"#command-{seeded.CommandId}-access-restricted")
            .GetAttribute("aria-pressed")
            .ShouldBe("true");
        var moderators = cut.Find($"#command-{seeded.CommandId}-access-moderators");
        moderators.GetAttribute("aria-pressed").ShouldBe("true");
        moderators.GetAttribute("data-selected").ShouldBe("true");
        cut.Find("button[aria-label='Save custom commands']")
            .GetAttribute("data-save-state")
            .ShouldBe("dirty");
        var fault = logger.Entries.ShouldHaveSingleItem();
        fault.Properties["UiOperation"].ShouldBe("SaveAsync");
        fault.Properties["FailureType"].ShouldBe(typeof(IOException).FullName);
    }

    [Test]
    public async Task ActionKind_ChangingToOverlayCue_ShowsTheBoundedEditor()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("[data-action-kind='OverlayCue']").Click();

        _ = cut.Find("[data-overlay-cue-command]").ShouldNotBeNull();
        _ = cut.Find($"#command-{seeded.CommandId}-overlay-target").ShouldNotBeNull();
        _ = cut.Find($"#command-{seeded.CommandId}-overlay-cue").ShouldNotBeNull();
        _ = cut.Find($"#command-{seeded.CommandId}-queue-policy").ShouldNotBeNull();
        _ = cut.Find($"#command-{seeded.CommandId}-reply-order").ShouldNotBeNull();
        cut.Find("button[data-action='test-overlay-cue-command']")
            .HasAttribute("disabled")
            .ShouldBeTrue();
    }

    [Test]
    public async Task StoredAutomationAction_EditingFineTuning_UsesUnavailableRecoveryAndPreservesIt()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var command = await db
                .CustomCommands.Include(value => value.Action)
                .SingleAsync(value => value.Id == seeded.CommandId);
            _ = db.CustomCommandActions.Remove(command.Action);
            _ = await db.SaveChangesAsync();
            command.Action = new AutomationCustomCommandAction { HostId = seeded.HostId };
            _ = await db.SaveChangesAsync();
        }
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.FindAll("[data-action-kind='Automation']").ShouldBeEmpty();
        cut.FindAll(".studio-choice-card[aria-pressed='true']").ShouldBeEmpty();
        _ = cut.Find("[data-unavailable-command-action]");
        cut.FindAll($"#command-{seeded.CommandId}-0-argument-reply").ShouldBeEmpty();
        cut.FindAll("select[data-flow-picker]").ShouldBeEmpty();
        cut.Find($"#command-{seeded.CommandId}-cooldown").Change("15");
        cut.Find($"#command-{seeded.CommandId}-invocation-limit")
            .Change(CustomCommandInvocationLimit.OncePerUser.ToString());
        cut.Find("button[aria-label='Save custom commands']").Click();

        await using var saved = await dbFactory.CreateDbContextAsync();
        (
            await saved
                .CustomCommandActions.OfType<AutomationCustomCommandAction>()
                .CountAsync(action => action.CustomCommandId == seeded.CommandId)
        ).ShouldBe(1);
        (
            await saved
                .CustomCommands.Where(command => command.Id == seeded.CommandId)
                .Select(command => command.CooldownSeconds)
                .SingleAsync()
        ).ShouldBe(15);
    }

    [Test]
    public async Task NewAnnouncement_Adding_SavesWithoutValidation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("button[data-action='add-scheduled-message']").Click();

        _ = cut.Find("[data-selected-editor='scheduled-message']").ShouldNotBeNull();
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
    public async Task HiddenValidationTarget_SavingInvalidEntity_RevealsAndFocusesTarget(
        ValidationSection section
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();
        var expected = InvalidateAndHideSection(cut, seeded, section);

        cut.Find("button[aria-label='Save custom commands']").Click();

        _ = cut.Find(expected.RevealSelector).ShouldNotBeNull();
        var control = cut.Find($"#{expected.ControlId}");
        control.GetAttribute("aria-invalid").ShouldBe("true");
        control.GetAttribute("aria-describedby").ShouldBe($"{expected.ControlId}-error");
        var focus = context.JSInterop.Invocations.Last(static invocation =>
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
        SelectTab(cut, "message-library");
        cut.Find("button[data-action='edit-reply']").Click();
        cut.Find("textarea").Input(string.Empty);
        SelectTab(cut, "commands");
        cut.Find("button[data-action='edit-scheduled-message']").Click();
        cut.Find("button[aria-controls='custom-announcement-delivery-details']").Click();
        cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay").Change("0");
        cut.Find($"#announcement-{seeded.AnnouncementId}-occurrence-lifetime").Change("61");

        cut.Find("button[aria-label='Save custom commands']").Click();

        ValidationMessages(cut).Length.ShouldBe(3);
        cut.Find(".studio").GetAttribute("data-active-fragment").ShouldBe("message-library");
        var invalidMessage = cut.Find("textarea");
        invalidMessage.GetAttribute("aria-invalid").ShouldBe("true");
        _ = invalidMessage.GetAttribute("aria-describedby").ShouldNotBeNull();
        SelectTab(cut, "commands");
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

        SelectTab(cut, "message-library");
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
            _ = db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = seeded.HostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            _ = await db.SaveChangesAsync();
        }

        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find($"#command-{seeded.CommandId}-aliases").Input("points");
        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.Find(".studio").GetAttribute("data-active-fragment").ShouldBe("commands");
        var aliases = cut.Find($"#command-{seeded.CommandId}-aliases");
        aliases.GetAttribute("aria-invalid").ShouldBe("true");
        _ = aliases.GetAttribute("aria-describedby").ShouldNotBeNull();
        ValidationMessages(cut).Length.ShouldBe(1);
        toasts.Current.ShouldHaveSingleItem().Kind.ShouldBe(ToastKind.Error);
        cut.Find("button[aria-label='Save custom commands']")
            .GetAttribute("data-save-state")
            .ShouldBe("dirty");
        await using var savedDb = await dbFactory.CreateDbContextAsync();
        (await savedDb.CustomCommandAliases.SingleAsync()).Alias.ShouldBe("command");
    }

    [Test]
    public async Task ConfigurationRefresh_DiscardsUnsavedInPageDrafts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find($"#command-{seeded.CommandId}-name").Input("Unsaved command");

        _ = await context
            .Services.GetRequiredService<EventBus<AppEventKind>>()
            .PublishAsync(AppEventKind.CustomCommandsChanged, CancellationToken.None);

        cut.Find($"#command-{seeded.CommandId}-name").GetAttribute("value").ShouldBe("Command");
        cut.Find("button[aria-label='Save custom commands']")
            .GetAttribute("data-save-state")
            .ShouldBe("clean");
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
        var counterName = cut.Find($"#counter-{seeded.CounterId}-name");
        context
            .JSInterop.VerifyFocusAsyncInvoke()
            .Arguments[0]
            .ShouldBeElementReferenceTo(counterName);
        cut.Find("button[data-action='edit-command']").Click();
        cut.Find($"#command-{seeded.CommandId}-name")
            .GetAttribute("value")
            .ShouldBe("Unsaved command");
    }

    [Test]
    public async Task RepeatedItemSelection_NameValidationStillIssuesANewerFocusRequest()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        for (var selection = 0; selection < 3; selection++)
        {
            cut.Find("button[data-action='edit-counter']").Click();
            cut.Find("button[data-action='edit-command']").Click();
        }

        var name = cut.Find($"#command-{seeded.CommandId}-name");
        name.Input(string.Empty);
        var focusCountBeforeValidation = context.JSInterop.Invocations.Count(static invocation =>
            invocation.Identifier == "Blazor._internal.domWrapper.focus"
        );
        var expectedNameReference = context
            .JSInterop.Invocations.Last(static invocation =>
                invocation.Identifier == "Blazor._internal.domWrapper.focus"
            )
            .Arguments[0];

        cut.Find("button[aria-label='Save custom commands']").Click();

        var focusInvocations = context
            .JSInterop.Invocations.Where(static invocation =>
                invocation.Identifier == "Blazor._internal.domWrapper.focus"
            )
            .ToArray();
        focusInvocations.Length.ShouldBe(focusCountBeforeValidation + 1);
        focusInvocations[^1].Arguments[0].ShouldBe(expectedNameReference);
        cut.Find($"#command-{seeded.CommandId}-name").GetAttribute("aria-invalid").ShouldBe("true");
    }

    [Test]
    public async Task EditingAndSaving_HighlightsEnablesThenClearsSaveState()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();
        var save = cut.Find("button[aria-label='Save custom commands']");
        save.HasAttribute("disabled").ShouldBeTrue();
        save.GetAttribute("data-save-state").ShouldBe("clean");

        cut.Find($"#command-{seeded.CommandId}-name").Input("Updated command");

        save = cut.Find("button[aria-label='Save custom commands']");
        save.HasAttribute("disabled").ShouldBeFalse();
        save.GetAttribute("data-save-state").ShouldBe("dirty");
        save.Click();

        save = cut.Find("button[aria-label='Save custom commands']");
        save.HasAttribute("disabled").ShouldBeTrue();
        save.GetAttribute("data-save-state").ShouldBe("clean");
    }

    [Test]
    public async Task AdvancedValidation_SelectsItemRevealsSectionAndFocusesField()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find("button[data-action='edit-scheduled-message']").Click();
        cut.Find("button[aria-controls='custom-announcement-delivery-details']").Click();
        cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay").Change("0");
        cut.Find("button[data-action='edit-counter']").Click();

        cut.Find("button[aria-label='Save custom commands']").Click();

        _ = cut.Find("[data-selected-editor='scheduled-message']").ShouldNotBeNull();
        cut.Find("button[aria-controls='custom-announcement-delivery-details']")
            .GetAttribute("aria-expanded")
            .ShouldBe("true");
        var invalid = cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay");
        invalid.GetAttribute("aria-invalid").ShouldBe("true");
        context
            .JSInterop.Invocations.Last(static invocation =>
                invocation.Identifier == "Blazor._internal.domWrapper.focus"
            )
            .Arguments[0]
            .ShouldBeElementReferenceTo(invalid);
    }

    [Test]
    public async Task EmptyCommands_CreateReply_AddsSelectsAndFocusesTheReplyEditor()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedEmptyConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("button[data-action='create-reply']").Click();

        _ = cut.Find("button[data-action='edit-reply']").ShouldNotBeNull();
        var name = cut.Find("input[id^='message-entry-'][id$='-name']");
        name.GetAttribute("value").ShouldBe("New reply");
        context.JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(name);
    }

    /// <summary>
    /// Only the first stage is open after a selection, so an invalid fine-tuning field is
    /// unreachable until validation opens the stage that owns it.
    /// </summary>
    [Test]
    public async Task FineTuningValidation_SavingFromAnotherItem_ReopensThatStage()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find("button[aria-controls='studio-stage-fine-tuning']").Click();
        cut.Find($"#command-{seeded.CommandId}-cooldown").Change("-1");
        cut.Find("button[data-action='edit-counter']").Click();

        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.Find("button[aria-controls='studio-stage-basics']")
            .GetAttribute("aria-expanded")
            .ShouldBe("true");
        cut.Find("button[aria-controls='studio-stage-fine-tuning']")
            .GetAttribute("aria-expanded")
            .ShouldBe("true");
        cut.Find($"#command-{seeded.CommandId}-cooldown")
            .GetAttribute("aria-invalid")
            .ShouldBe("true");
    }

    [Test]
    public async Task CommandsWithoutReplies_OffersDirectMessageLibraryAction()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedEmptyConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("button[data-action='create-reply']").Click();

        cut.Find(".studio").GetAttribute("data-active-fragment").ShouldBe("message-library");
        _ = cut.Find("[data-selected-editor='reply']").ShouldNotBeNull();
    }

    [Test]
    public async Task InvocationLimit_EditingAndResettingAsSelectedHostManager_RoundTripsAndAudits()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.CustomCommandInvocationClaims.Add(
                new CustomCommandInvocationClaim
                {
                    HostId = seeded.HostId,
                    CustomCommandId = seeded.CommandId,
                    TwitchUserId = "viewer-id",
                    ClaimedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }

        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("button[aria-controls='studio-stage-fine-tuning']").Click();
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
        cut.Find("button[aria-controls='studio-stage-fine-tuning']").Click();
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

    private static string[] ValidationMessages(
        IRenderedComponent<CustomCommandSettingsPage> page
    ) =>
        page.Find("[data-validation-summary]")
            .QuerySelectorAll("li")
            .Select(static item => item.TextContent.Trim())
            .ToArray();

    private static ValidationSectionExpectation InvalidateAndHideSection(
        IRenderedComponent<CustomCommandSettingsPage> page,
        SeededConfiguration seeded,
        ValidationSection section
    ) =>
        section switch
        {
            ValidationSection.Replies => InvalidateReply(page, seeded),
            ValidationSection.Commands => InvalidateCommand(page, seeded),
            ValidationSection.Counters => InvalidateCounter(page, seeded),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };

    private static ValidationSectionExpectation InvalidateReply(
        IRenderedComponent<CustomCommandSettingsPage> page,
        SeededConfiguration seeded
    )
    {
        SelectTab(page, "message-library");
        page.Find("button[data-action='edit-reply']").Click();
        var controlId = $"message-entry-{seeded.MessageEntryId}-name";
        page.Find($"#{controlId}").Input(string.Empty);
        SelectTab(page, "commands");
        page.Find("button[data-action='edit-counter']").Click();
        page.FindAll($"#{controlId}").ShouldBeEmpty();
        return new("[data-selected-editor='reply']", controlId);
    }

    private static void SelectTab(IRenderedComponent<CustomCommandSettingsPage> page, string key) =>
        page.Find($"#custom-command-{key}-tab").Click();

    private static ValidationSectionExpectation InvalidateCommand(
        IRenderedComponent<CustomCommandSettingsPage> page,
        SeededConfiguration seeded
    )
    {
        var controlId = $"command-{seeded.CommandId}-name";
        page.Find($"#{controlId}").Input(string.Empty);
        page.Find("button[data-action='edit-counter']").Click();
        page.FindAll($"#{controlId}").ShouldBeEmpty();
        return new("[data-selected-editor='command']", controlId);
    }

    private static ValidationSectionExpectation InvalidateCounter(
        IRenderedComponent<CustomCommandSettingsPage> page,
        SeededConfiguration seeded
    )
    {
        page.Find("button[data-action='edit-counter']").Click();
        var controlId = $"counter-{seeded.CounterId}-name";
        page.Find($"#{controlId}").Input(string.Empty);
        page.Find("button[data-action='edit-command']").Click();
        page.FindAll($"#{controlId}").ShouldBeEmpty();
        return new("[data-selected-editor='counter']", controlId);
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
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
        _ = await db.SaveChangesAsync();
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
        _ = await db.SaveChangesAsync();
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed record SeededConfiguration(
        int HostId,
        int MessageEntryId,
        int CommandId,
        int CounterId,
        int AnnouncementId
    );

    private sealed class FailOnceDbContextFactory(IDbContextFactory<BlokeBotDbContext> inner)
        : IDbContextFactory<BlokeBotDbContext>
    {
        private bool _failed;

        public BlokeBotDbContext CreateDbContext()
        {
            FailOnce();
            return inner.CreateDbContext();
        }

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            FailOnce();
            return inner.CreateDbContextAsync(cancellationToken);
        }

        private void FailOnce()
        {
            if (_failed)
            {
                return;
            }

            _failed = true;
            throw new IOException("Expected test load failure.");
        }
    }

    private sealed class ArmableDbContextFactory(IDbContextFactory<BlokeBotDbContext> inner)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public Exception? Fault { get; set; }

        public BlokeBotDbContext CreateDbContext()
        {
            ThrowIfFaulted();
            return inner.CreateDbContext();
        }

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            ThrowIfFaulted();
            return inner.CreateDbContextAsync(cancellationToken);
        }

        private void ThrowIfFaulted()
        {
            if (Fault is not null)
            {
                throw Fault;
            }
        }
    }

    private sealed class QueueViewerResolver(params CustomCommandViewerResolution[] resolutions)
        : ICustomCommandViewerResolver
    {
        private readonly Queue<CustomCommandViewerResolution> _resolutions = new(resolutions);

        public Task<CustomCommandViewerResolution> ResolveAsync(
            string login,
            CancellationToken ct
        ) => Task.FromResult(_resolutions.Dequeue());
    }

    public enum ValidationSection
    {
        Replies,
        Commands,
        Counters,
    }

    private sealed record ValidationSectionExpectation(string RevealSelector, string ControlId);
}
