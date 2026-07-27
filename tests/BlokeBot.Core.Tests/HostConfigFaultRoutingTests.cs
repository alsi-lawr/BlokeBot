using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Channels;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Admin.Authorization;
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
        await OpenDisclosureAsync(page, "Use your own bot account");

        page.WaitForAssertion(() =>
        {
            var customBotToggle = page.Find("#custom-bot input[type='checkbox']");
            customBotToggle.GetAttribute("disabled").ShouldBeNull();
            page.Markup.ShouldContain("The channel owner must connect this Twitch account.");
            page.Markup.ShouldNotContain("/oauth/channel-bot/start");
        });
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
        page.WaitForAssertion(() => tokens.RequestCount.ShouldBe(1));
        var secondClick = ClickAccessModeAsync(page, "Allowed list only");
        page.WaitForAssertion(() => tokens.RequestCount.ShouldBe(2));
        await ClickAccessModeAsync(page, "All mods");

        second.SetResult("app-token");
        await secondClick;
        first.SetResult("app-token");
        await firstClick;

        page.WaitForAssertion(() => AssertAccessMode(page, allowModsByDefault: true));
        clock.Advance(TimeSpan.FromMilliseconds(180));
        (await ReadAllowModsByDefaultAsync(dbFactory, hostId)).ShouldBeTrue();
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

        page.WaitForAssertion(() => AssertAccessMode(page, allowModsByDefault: true));
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

    private static async Task ClickAccessModeAsync<TComponent>(
        IRenderedComponent<TComponent> page,
        string text
    )
        where TComponent : IComponent
    {
        await OpenDisclosureAsync(page, "Moderator help");
        await page.InvokeAsync(() =>
            page.FindAll("button")
                .Single(button => button.TextContent.Trim() == text)
                .ClickAsync(new())
        );
    }

    private static async Task OpenDisclosureAsync<TComponent>(
        IRenderedComponent<TComponent> page,
        string title
    )
        where TComponent : IComponent
    {
        page.WaitForAssertion(() =>
            page.FindAll("button.disclosure-trigger")
                .Count(button => button.TextContent.Contains(title, StringComparison.Ordinal))
                .ShouldBe(1)
        );
        var trigger = page.FindAll("button.disclosure-trigger")
            .Single(button => button.TextContent.Contains(title, StringComparison.Ordinal));
        var contentId = trigger.GetAttribute("aria-controls");
        contentId.ShouldNotBeNullOrWhiteSpace();
        var content = page.Find($"#{contentId}");
        if (!content.HasAttribute("hidden"))
        {
            return;
        }

        await page.InvokeAsync(() =>
            page.FindAll("button.disclosure-trigger")
                .Single(button => button.TextContent.Contains(title, StringComparison.Ordinal))
                .ClickAsync(new())
        );
        page.Find($"#{contentId}").HasAttribute("hidden").ShouldBeFalse();
        page.FindAll("button.disclosure-trigger")
            .Single(button => button.TextContent.Contains(title, StringComparison.Ordinal))
            .GetAttribute("aria-expanded")
            .ShouldBe("true");
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

    private sealed class ScriptedAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        private readonly Queue<Task<string>> _tokens = [];

        public int RequestCount { get; private set; }

        public void Enqueue(Task<string> token)
        {
            _tokens.Enqueue(token);
        }

        public TaskCompletionSource<string> EnqueuePending()
        {
            var pending = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _tokens.Enqueue(pending.Task);
            return pending;
        }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            RequestCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return _tokens.Dequeue();
        }
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
