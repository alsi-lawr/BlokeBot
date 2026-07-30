using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Channels;
using AngleSharp.Dom;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class HostConfigFaultRoutingTests
{
    [Test]
    public async Task ViewerCommandsDisclosure_OpeningAndEvents_PreserveDirtyHostDrafts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
            host.CommandsAliasesConfigured = true;
            db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Commands,
                    Alias = "commands",
                }
            );
            await db.SaveChangesAsync();
        }

        var testContext = UiTestContextFactory.CreateWithAuthorization(dbFactory, hostId);
        await using var context = testContext.Context;
        ConfigureHostServices(
            context,
            dbFactory,
            new RecordingLogger<UiFaultTelemetry>(),
            new ManualTimeProvider()
        );
        var page = RenderHostConfigPage(context);

        page.WaitForAssertion(() =>
        {
            page.Find("#commands-aliases").GetAttribute("value").ShouldBe("commands");
            AvailableCommandsButton(page).GetAttribute("aria-expanded").ShouldBe("false");
        });
        page.Find("#commands-aliases").Input("unsaved-catalog");
        page.Find("#startup-chat-message").Input("unsaved startup");

        await page.InvokeAsync(() => AvailableCommandsButton(page).ClickAsync(new()));

        page.WaitForAssertion(() =>
        {
            AvailableCommandsButton(page).GetAttribute("aria-expanded").ShouldBe("true");
            page.Find("[data-command-catalog]").TextContent.ShouldContain("!commands");
            page.Find("#commands-aliases").GetAttribute("value").ShouldBe("unsaved-catalog");
            page.Find("#startup-chat-message").GetAttribute("value").ShouldBe("unsaved startup");
        });

        await context
            .Services.GetRequiredService<EventBus<AppEventKind>>()
            .PublishAsync(AppEventKind.PointsChanged, CancellationToken.None);
        await context
            .Services.GetRequiredService<EventBus<AppEventKind>>()
            .PublishAsync(AppEventKind.HostedChannelsChanged, CancellationToken.None);

        page.WaitForAssertion(() =>
        {
            page.Find("#commands-aliases").GetAttribute("value").ShouldBe("unsaved-catalog");
            page.Find("#startup-chat-message").GetAttribute("value").ShouldBe("unsaved startup");
        });
    }

    [Test]
    public async Task ViewerCommandConflictAlerts_RenderWithDarkThemeAwareForeground()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
            host.CommandsDefaultConflictAlias = "commands";
            await db.SaveChangesAsync();
        }

        var testContext = UiTestContextFactory.CreateWithAuthorization(dbFactory, hostId);
        await using var context = testContext.Context;
        ConfigureHostServices(
            context,
            dbFactory,
            new RecordingLogger<UiFaultTelemetry>(),
            new ManualTimeProvider()
        );
        var page = RenderHostConfigPage(context);

        page.WaitForAssertion(() =>
        {
            var alert = page.Find("[data-command-conflict]");
            alert.ClassList.ShouldContain("text-amber-700");
            alert.ClassList.ShouldNotContain("text-amber-900");
        });

        await page.InvokeAsync(() => AvailableCommandsButton(page).ClickAsync(new()));

        page.WaitForAssertion(() =>
        {
            var alert = page.Find("[data-command-catalog-conflicts]");
            alert.ClassList.ShouldContain("text-amber-700");
            alert.ClassList.ShouldNotContain("text-amber-900");
        });
    }

    [Test]
    public async Task AdminImpersonation_RenderingHostConfig_ShowsManagementWithoutOwnerOAuth()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        var testContext = UiTestContextFactory.CreateWithAuthorization(dbFactory, hostId);
        await using var context = testContext.Context;
        ConfigureHostServices(
            context,
            dbFactory,
            new RecordingLogger<UiFaultTelemetry>(),
            new ManualTimeProvider()
        );
        SetAdminClaims(testContext.Authorization, hostId);

        var page = RenderHostConfigPage(context);

        page.WaitForAssertion(() =>
        {
            var customBotToggle = page.Find("#custom-bot input[type='checkbox']");
            customBotToggle.GetAttribute("disabled").ShouldBeNull();
            page.Markup.ShouldContain("The channel owner must connect this Twitch account.");
            page.Markup.ShouldNotContain("/oauth/channel-bot/start");
        });
    }

    [Test]
    public async Task FragmentNavigation_RevealsNamedTargetWithoutDiscardingDirtyStartupMessage()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        var testContext = UiTestContextFactory.CreateWithAuthorization(dbFactory, hostId);
        await using var context = testContext.Context;
        ConfigureHostServices(
            context,
            dbFactory,
            new RecordingLogger<UiFaultTelemetry>(),
            new ManualTimeProvider()
        );
        var module = context.JSInterop.SetupModule("./Components/CollapsibleSection.razor.js");
        module.SetupVoid("focusElement", _ => true).SetVoidResult();
        var fragmentModule = context.JSInterop.SetupModule(
            "./Features/HostConfig/Page/HostConfigFragmentObserver.razor.js"
        );
        fragmentModule.SetupVoid("observe", _ => true).SetVoidResult();
        fragmentModule.SetupVoid("dispose", _ => true).SetVoidResult();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/host");

        var page = RenderHostConfigPage(context);
        var observer = page.FindComponent<HostConfigFragmentObserver>();

        page.Find("#startup-chat-message").Input("unsaved startup message");
        foreach (var fragment in new[] { "chat-tools", "moderator-help", "bot-status" })
        {
            await observer.InvokeAsync(() =>
                observer.Instance.NotifyFragmentChangedAsync(
                    $"http://localhost/host?simulationTheme=light#{fragment}"
                )
            );

            page.WaitForAssertion(() =>
            {
                page.Find("#startup-chat-message")
                    .GetAttribute("value")
                    .ShouldBe("unsaved startup message");
                module
                    .Invocations.Count(invocation =>
                        invocation.Identifier == "focusElement"
                        && invocation.Arguments.Single()?.ToString() == fragment
                    )
                    .ShouldBe(1);
            });
        }

        page.Find("#chat-tools").GetAttribute("aria-label").ShouldBe("Chat tools");
        page.Find("#moderator-help").GetAttribute("aria-label").ShouldBe("Moderator help");
        page.Find("#bot-status").GetAttribute("aria-label").ShouldBe("Bot status");
        fragmentModule
            .Invocations.Count(invocation => invocation.Identifier == "observe")
            .ShouldBe(1);
    }

    [Test]
    public async Task IndependentChatToolCard_Toggling_UpdatesOnlyTheSelectedFeature()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        var testContext = UiTestContextFactory.CreateWithAuthorization(dbFactory, hostId);
        await using var context = testContext.Context;
        ConfigureHostServices(
            context,
            dbFactory,
            new RecordingLogger<UiFaultTelemetry>(),
            new ManualTimeProvider()
        );

        var page = RenderHostConfigPage(context);
        page.WaitForAssertion(() =>
        {
            var shoutouts = FindFeatureButton(page, "Shoutouts");
            shoutouts.HasAttribute("aria-pressed").ShouldBeTrue();
            shoutouts.TextContent.ShouldContain("manual and automatic raid shoutouts");
            page.FindAll(".feature-toggle-card").Count.ShouldBe(12);
            var overlays = FindFeatureButton(page, "Overlays");
            overlays.HasAttribute("aria-pressed").ShouldBeTrue();
            overlays.QuerySelector("svg").ShouldNotBeNull();
        });
        page.Find("#startup-chat-message").Input("unsaved Native switch draft");

        await page.InvokeAsync(() => FindFeatureButton(page, "Shoutouts").ClickAsync(new()));

        page.WaitForAssertion(() =>
        {
            FindFeatureButton(page, "Shoutouts").HasAttribute("aria-pressed").ShouldBeFalse();
            page.Find("#startup-chat-message")
                .GetAttribute("value")
                .ShouldBe("unsaved Native switch draft");
        });

        await page.InvokeAsync(() => FindFeatureButton(page, "Overlays").ClickAsync(new()));
        page.WaitForAssertion(() =>
        {
            FindFeatureButton(page, "Overlays").HasAttribute("aria-pressed").ShouldBeFalse();
            var toast = context
                .Services.GetRequiredService<ToastService>()
                .Current.Single(value => value.Title == "Overlays disabled");
            toast.Message.ShouldBe(
                "Overlays is now disabled for #streamer. Its dashboard and Browser Sources are unavailable until you enable it again."
            );
            toast.Message.ShouldNotContain("chat commands");
        });

        await page.InvokeAsync(() => FindFeatureButton(page, "Overlays").ClickAsync(new()));
        page.WaitForAssertion(() =>
        {
            FindFeatureButton(page, "Overlays").HasAttribute("aria-pressed").ShouldBeTrue();
            var toast = context
                .Services.GetRequiredService<ToastService>()
                .Current.Single(value => value.Title == "Overlays enabled");
            toast.Message.ShouldBe(
                "Overlays is now enabled for #streamer. Its dashboard and Browser Sources are available again."
            );
            toast.Message.ShouldNotContain("chat commands");
        });

        await using var verify = await dbFactory.CreateDbContextAsync();
        var enabled = await verify
            .Hosts.Where(host => host.Id == hostId)
            .Select(host => host.EnabledFeatures)
            .SingleAsync();
        enabled.Contains(HostFeatureFlags.Shoutouts).ShouldBeFalse();
        enabled.Contains(HostFeatureFlags.Overlays).ShouldBeTrue();
    }

    [Test]
    public async Task UnavailableAuthority_PolicyModeRemainsUnchangedUntilSameChoiceCanBeSaved()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        var testContext = UiTestContextFactory.CreateWithAuthorization(dbFactory, hostId);
        await using var context = testContext.Context;
        var clock = new ManualTimeProvider();
        var tokens = new ScriptedAppAccessTokenSource();
        tokens.Enqueue(Task.FromException<string>(new TimeoutException()));
        tokens.Enqueue(Task.FromResult("app-token"));
        ConfigureHostServices(context, dbFactory, new RecordingLogger<UiFaultTelemetry>(), clock);
        ConfigureModeratorAuthorityServices(context, tokens);

        var page = RenderHostConfigPage(context);
        SetModeratorClaims(testContext.Authorization, hostId);

        await ClickAccessModeAsync(page, "Allowed list only");

        AssertAccessMode(page, allowModsByDefault: true);
        (await ReadAllowModsByDefaultAsync(dbFactory, hostId)).ShouldBeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(180));
        (await ReadAllowModsByDefaultAsync(dbFactory, hostId)).ShouldBeTrue();

        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var savedSubscription = context
            .Services.GetRequiredService<EventBus<AppEventKind>>()
            .Subscribe(
                AppEventKind.HostedChannelsChanged,
                ObserverIdentity.Named("Test.HostConfig.UnavailableAuthority"),
                (_, _) =>
                {
                    saved.TrySetResult();
                    return ValueTask.CompletedTask;
                }
            );
        await ClickAccessModeAsync(page, "Allowed list only");
        AssertAccessMode(page, allowModsByDefault: false);
        clock.Advance(TimeSpan.FromMilliseconds(180));
        await saved.Task;
        AssertAccessMode(page, allowModsByDefault: false);
        (await ReadAllowModsByDefaultAsync(dbFactory, hostId)).ShouldBeFalse();
    }

    [Test]
    public async Task ReorderedAuthorityCompletions_KeepLatestPolicyIntentAndDoNotSaveStaleGrant()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        var testContext = UiTestContextFactory.CreateWithAuthorization(dbFactory, hostId);
        await using var context = testContext.Context;
        var clock = new ManualTimeProvider();
        var tokens = new ScriptedAppAccessTokenSource();
        var first = tokens.EnqueuePending();
        var second = tokens.EnqueuePending();
        ConfigureHostServices(context, dbFactory, new RecordingLogger<UiFaultTelemetry>(), clock);
        ConfigureModeratorAuthorityServices(context, tokens);

        var page = RenderHostConfigPage(context);
        SetModeratorClaims(testContext.Authorization, hostId);

        var firstClick = ClickAccessModeAsync(page, "Allowed list only");
        await first.Started.Task;
        var secondClick = ClickAccessModeAsync(page, "Allowed list only");
        await second.Started.Task;
        await ClickAccessModeAsync(page, "All mods");

        second.Completion.SetResult("app-token");
        await secondClick;
        first.Completion.SetResult("app-token");
        await firstClick;

        AssertAccessMode(page, allowModsByDefault: true);
        clock.Advance(TimeSpan.FromMilliseconds(180));
        (await ReadAllowModsByDefaultAsync(dbFactory, hostId)).ShouldBeTrue();
    }

    [Test]
    public async Task DetachedSave_Faulting_RedactsTelemetryAndReachesErrorBoundary()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var faultingDbFactory = new FaultingDbContextFactory(dbFactory);
        var logger = new RecordingLogger<UiFaultTelemetry>();
        var clock = new ManualTimeProvider();
        ConfigureHostServices(context, faultingDbFactory, logger, clock);
        context.ComponentFactories.AddStub<HostBotChannelStatusPanel>();
        RenderFragment content = builder =>
        {
            builder.OpenComponent<HostConfigPage>(0);
            builder.CloseComponent();
        };
        var boundary = context.Render<CapturingErrorBoundary>(parameters =>
            parameters.Add(x => x.ChildContent, content)
        );
        await ClickAccessModeAsync(boundary, "Allowed list only");
        const string SensitiveMessage = "secret-host-config-failure";
        var exception = new InvalidOperationException(SensitiveMessage);
        faultingDbFactory.Failure = exception;

        clock.Advance(TimeSpan.FromMilliseconds(180));

        boundary.WaitForAssertion(
            () => boundary.Instance.CapturedException.ShouldBeSameAs(exception),
            TimeSpan.FromSeconds(5)
        );
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Properties["UiComponent"].ShouldBe(nameof(HostConfigPage));
        entry.Properties["UiOperation"].ShouldBe("PersistAllowModsByDefaultAsync");
        entry.Properties["HostId"].ShouldBe(hostId);
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
        entry.Message.ShouldNotContain(SensitiveMessage);
        context.Services.GetRequiredService<ToastService>().Current.ShouldBeEmpty();
    }

    [Test]
    public async Task CurrentKnownFailures_Completing_RollBackExactSnapshotWithTypedFeedback()
    {
        await AssertCurrentFailureAsync(runtimeNotificationFails: false);
        await AssertCurrentFailureAsync(runtimeNotificationFails: true);
    }

    private static void ConfigureHostServices(
        BunitContext context,
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        ILogger<UiFaultTelemetry> logger,
        TimeProvider clock
    )
    {
        context.Services.AddSingleton(dbFactory);
        context.Services.AddSingleton(clock);
        context.Services.AddSingleton<IOptions<BlokeBotOptions>>(
            Options.Create(new BlokeBotOptions())
        );
        context.Services.AddSingleton(BotSettings.FromOptions(new BotOptions()));
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        context.Services.AddOAuthTransport();
        context.Services.AddHelix();
        context.Services.AddBlokeBotAppCommands();
        context.Services.AddBlokeBotSiteAccess(AccessListProfileEnrichmentMode.Disabled);
        context.Services.AddBlokeBotAdmin(BotAccountAuthorizationMode.Disabled);
        context.Services.AddBlokeBotHostedChannels(HostBotAppAccessTokenMode.Unavailable);
        context.Services.AddBlokeBotHosts();
        context.Services.AddTransient<ChannelBotOAuthService>();
        context.Services.AddSingleton(new UiFaultTelemetry(logger));
    }

    private static void ConfigureModeratorAuthorityServices(
        BunitContext context,
        IHostBotAppAccessTokenSource tokens
    )
    {
        context.Services.AddSingleton<ModeratorAuthorityService>(
            serviceProvider => new ModeratorAuthorityService(
                tokens,
                new HelixClient(
                    new ModeratedChannelsHttpClientFactory(),
                    global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
                ),
                serviceProvider.GetRequiredService<BotSettings>(),
                serviceProvider.GetRequiredService<HostModAccessService>(),
                serviceProvider.GetRequiredService<TimeProvider>()
            )
        );
    }

    private static void SetModeratorClaims(BunitAuthorizationContext authorization, int hostId)
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", AuthRole.Moderator);
        authorization.SetClaims(
            TestPrincipals
                .BlokeBotUser(
                    "moderator",
                    role: AuthRole.Moderator,
                    availableHosts: [host],
                    selectedHost: host
                )
                .Claims.ToArray()
        );
    }

    private static void SetAdminClaims(BunitAuthorizationContext authorization, int hostId)
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", AuthRole.Admin);
        authorization.SetClaims(
            TestPrincipals
                .BlokeBotUser(
                    "administrator",
                    isBotAdmin: true,
                    availableHosts: [host],
                    selectedHost: host
                )
                .Claims.Append(new Claim(BotHostClaims.AdminEditingLogin, "administrator"))
                .ToArray()
        );
    }

    private static async Task AssertCurrentFailureAsync(bool runtimeNotificationFails)
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var clock = new ManualTimeProvider();
        ConfigureHostServices(context, dbFactory, new RecordingLogger<UiFaultTelemetry>(), clock);
        TestEventBusRecording<AppEventKind>? intentionalEventing = null;
        if (runtimeNotificationFails)
        {
            intentionalEventing = TestEventBus.CreateContinueAndRecord<AppEventKind>();
            context.Services.AddSingleton(intentionalEventing.Events);
            intentionalEventing.Events.Subscribe(
                AppEventKind.HostedChannelsChanged,
                ObserverIdentity.Named("Test.HostConfig.CurrentFailure"),
                (_, _) =>
                    ValueTask.FromException(new InvalidOperationException("runtime unavailable"))
            );
        }

        var page = RenderHostConfigPage(context);
        if (!runtimeNotificationFails)
        {
            await DeleteHostAsync(dbFactory, hostId);
        }

        var toasts = context.Services.GetRequiredService<ToastService>();
        var toastPublished = Channel.CreateUnbounded<bool>();
        toasts.Changed += () => toastPublished.Writer.TryWrite(true);

        await ClickAccessModeAsync(page, "Allowed list only");
        clock.Advance(TimeSpan.FromMilliseconds(180));
        _ = await toastPublished.Reader.ReadAsync();

        await page.InvokeAsync(() => AssertAccessMode(page, allowModsByDefault: true));
        page.Markup.ShouldContain("allowedmod");
        page.Markup.ShouldContain("blockedmod");
        var moderatorToggle = page.FindAll("label")
            .Single(label =>
                label.TextContent.Contains("Let moderators help", StringComparison.Ordinal)
            )
            .QuerySelector("input");
        moderatorToggle.ShouldNotBeNull();
        moderatorToggle.HasAttribute("checked").ShouldBeTrue();
        var toast = toasts.Current.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.Error);
        toast.Title.ShouldBe("Mod help not saved");
        toast.Message.ShouldBe(
            runtimeNotificationFails
                ? new HostModAccessSaveFailure.RuntimeNotificationFailed(1, 1).Message
                : new HostModAccessSaveFailure.HostNotFound().Message
        );
        if (intentionalEventing is not null)
        {
            intentionalEventing.Reports.Count.ShouldBe(2);
            intentionalEventing.Reports.ShouldAllBe(report =>
                report.Observer == ObserverIdentity.Named("Test.HostConfig.CurrentFailure")
                && report.FailureType == typeof(InvalidOperationException).FullName
            );
        }
    }

    private static IRenderedComponent<HostConfigPage> RenderHostConfigPage(BunitContext context)
    {
        context.ComponentFactories.AddStub<HostBotChannelStatusPanel>();
        return context.Render<HostConfigPage>();
    }

    private static IElement AvailableCommandsButton(IRenderedComponent<HostConfigPage> page)
    {
        return page.FindAll("button")
            .Single(button =>
                button.TextContent.Contains("Available viewer commands", StringComparison.Ordinal)
            );
    }

    private static IElement FindFeatureButton(
        IRenderedComponent<HostConfigPage> page,
        string featureName
    )
    {
        return page.FindAll("#chat-tools button")
            .Single(button => button.TextContent.Contains(featureName, StringComparison.Ordinal));
    }

    private static Task ClickAccessModeAsync<TComponent>(
        IRenderedComponent<TComponent> page,
        string text
    )
        where TComponent : IComponent
    {
        return page.InvokeAsync(() =>
            page.FindAll("button")
                .Single(button => button.TextContent.Trim() == text)
                .ClickAsync(new())
        );
    }

    private static void AssertAccessMode<TComponent>(
        IRenderedComponent<TComponent> page,
        bool allowModsByDefault
    )
        where TComponent : IComponent
    {
        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "All mods")
            .HasAttribute("aria-pressed")
            .ShouldBe(allowModsByDefault);
        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Allowed list only")
            .HasAttribute("aria-pressed")
            .ShouldBe(!allowModsByDefault);
    }

    private static async Task DeleteHostAsync(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(x => x.Id == hostId);
        db.Hosts.Remove(host);
        await db.SaveChangesAsync();
    }

    private static async Task<bool> ReadAllowModsByDefaultAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db
            .HostModAccessSettings.Where(settings => settings.HostId == hostId)
            .Select(settings => settings.AllowModsByDefault)
            .SingleAsync();
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        bool includeAccessState = false
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        if (includeAccessState)
        {
            db.HostModAccessSettings.Add(
                new HostModAccessSettings
                {
                    HostId = host.Id,
                    ModsEnabled = true,
                    AllowModsByDefault = true,
                }
            );
            db.HostModAccessEntries.AddRange(
                new HostModAccessEntry
                {
                    HostId = host.Id,
                    Kind = AccessListEntryKind.Whitelist,
                    Login = "allowedmod",
                    CreatedAtUtc = DateTime.UtcNow,
                },
                new HostModAccessEntry
                {
                    HostId = host.Id,
                    Kind = AccessListEntryKind.Blacklist,
                    Login = "blockedmod",
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        return host.Id;
    }

    private sealed class FaultingDbContextFactory(IDbContextFactory<BlokeBotDbContext> innerFactory)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public Exception? Failure { get; set; }

        public BlokeBotDbContext CreateDbContext()
        {
            return Failure is null ? innerFactory.CreateDbContext() : throw Failure;
        }

        public ValueTask<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Failure is null
                ? new ValueTask<BlokeBotDbContext>(
                    innerFactory.CreateDbContextAsync(cancellationToken)
                )
                : ValueTask.FromException<BlokeBotDbContext>(Failure);
        }
    }

    private sealed class ScriptedAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        private readonly Queue<ScriptedTokenRequest> _tokens = [];

        public int RequestCount { get; private set; }

        public void Enqueue(Task<string> token)
        {
            _tokens.Enqueue(new(token, null));
        }

        public PendingTokenRequest EnqueuePending()
        {
            var pending = new PendingTokenRequest();
            _tokens.Enqueue(new(pending.Completion.Task, pending.Started));
            return pending;
        }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            RequestCount++;
            cancellationToken.ThrowIfCancellationRequested();
            var request = _tokens.Dequeue();
            request.Started?.TrySetResult();
            return request.Completion;
        }

        public sealed class PendingTokenRequest
        {
            public TaskCompletionSource Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<string> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed record ScriptedTokenRequest(
            Task<string> Completion,
            TaskCompletionSource? Started
        );
    }

    private sealed class ModeratedChannelsHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new Handler());
        }

        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"data":[{"broadcaster_login":"streamer"}],"pagination":{}}""",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
            }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _current = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _current;
            }
        }

        public override long GetTimestamp()
        {
            return GetUtcNow().UtcTicks;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _current = _current.Add(delta);
                due = _timers.Where(timer => timer.IsDue(_current)).ToList();
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private TimeSpan _period;
            private DateTimeOffset _dueAt = DateTimeOffset.MaxValue;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    _period = period;
                    _dueAt =
                        dueTime == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner._current.Add(dueTime);
                    if (!owner._timers.Contains(this))
                    {
                        owner._timers.Add(this);
                    }
                }

                if (dueTime != Timeout.InfiniteTimeSpan && dueTime <= TimeSpan.Zero)
                {
                    Fire();
                }

                return true;
            }

            public void Dispose()
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool IsDue(DateTimeOffset value)
            {
                lock (owner._gate)
                {
                    return !_disposed && _dueAt <= value;
                }
            }

            public void Fire()
            {
                lock (owner._gate)
                {
                    if (_disposed || _dueAt > owner._current)
                    {
                        return;
                    }

                    if (_period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan)
                    {
                        _dueAt = owner._current.Add(_period);
                    }
                    else
                    {
                        _disposed = true;
                        owner._timers.Remove(this);
                    }
                }

                callback(state);
            }
        }
    }
}
