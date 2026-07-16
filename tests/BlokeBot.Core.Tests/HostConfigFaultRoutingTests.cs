using System.Threading.Channels;
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
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Bunit;
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
        ClickAccessMode(boundary, "Allowed list only");
        const string SensitiveMessage = "secret-host-config-failure";
        var exception = new InvalidOperationException(SensitiveMessage);
        faultingDbFactory.Failure = exception;

        clock.Advance(TimeSpan.FromMilliseconds(180));

        boundary.WaitForAssertion(() =>
            boundary.Instance.CapturedException.ShouldBeSameAs(exception)
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
        context.Services.AddBlokeBotSiteAccess(AccessListProfileEnrichmentMode.Disabled);
        context.Services.AddBlokeBotAdmin(BotAccountAuthorizationMode.Disabled);
        context.Services.AddBlokeBotHostedChannels(HostBotAppAccessTokenMode.Unavailable);
        context.Services.AddBlokeBotHosts();
        context.Services.AddTransient<ChannelBotOAuthService>();
        context.Services.AddSingleton(new UiFaultTelemetry(logger));
    }

    private static async Task AssertStaleCompletionAsync(bool firstNotificationFails)
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var clock = new ManualTimeProvider();
        ConfigureHostServices(context, dbFactory, new RecordingLogger<UiFaultTelemetry>(), clock);
        var notifications = new NotificationGate();
        context
            .Services.GetRequiredService<EventBus<AppEventKind>>()
            .Subscribe(
                AppEventKind.HostedChannelsChanged,
                ObserverIdentity.Named(
                    firstNotificationFails
                        ? "Test.HostConfig.StaleFailure"
                        : "Test.HostConfig.StaleSuccess"
                ),
                notifications.ObserveAsync
            );
        var page = RenderHostConfigPage(context);

        ClickAccessMode(page, "Allowed list only");
        clock.Advance(TimeSpan.FromMilliseconds(180));
        (await notifications.WaitForEntryAsync()).ShouldBe(1);
        ClickAccessMode(page, "All mods");
        clock.Advance(TimeSpan.FromMilliseconds(180));
        notifications.Release(
            firstNotificationFails ? NotificationResult.Failure : NotificationResult.Success
        );
        if (firstNotificationFails)
        {
            (await notifications.WaitForEntryAsync()).ShouldBe(2);
            notifications.Release(NotificationResult.Success);
        }

        var finalNotification = firstNotificationFails ? 3 : 2;
        (await notifications.WaitForEntryAsync()).ShouldBe(finalNotification);

        AssertAccessMode(page, allowModsByDefault: true);
        context.Services.GetRequiredService<ToastService>().Current.ShouldBeEmpty();
        notifications.Release(NotificationResult.Success);
        await notifications.WaitForExitAsync(finalNotification);
    }

    private static async Task AssertCurrentFailureAsync(bool runtimeNotificationFails)
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, includeAccessState: true);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var clock = new ManualTimeProvider();
        ConfigureHostServices(context, dbFactory, new RecordingLogger<UiFaultTelemetry>(), clock);
        if (runtimeNotificationFails)
        {
            context
                .Services.GetRequiredService<EventBus<AppEventKind>>()
                .Subscribe(
                    AppEventKind.HostedChannelsChanged,
                    ObserverIdentity.Named("Test.HostConfig.CurrentFailure"),
                    (_, _) =>
                        ValueTask.FromException(
                            new InvalidOperationException("runtime unavailable")
                        )
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

        ClickAccessMode(page, "Allowed list only");
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
    }

    private static IRenderedComponent<HostConfigPage> RenderHostConfigPage(BunitContext context)
    {
        context.ComponentFactories.AddStub<HostBotChannelStatusPanel>();
        return context.Render<HostConfigPage>();
    }

    private static void ClickAccessMode<TComponent>(
        IRenderedComponent<TComponent> page,
        string text
    )
        where TComponent : IComponent
    {
        page.FindAll("button").Single(button => button.TextContent.Trim() == text).Click();
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

    private enum NotificationResult
    {
        Success,
        Failure,
    }

    private sealed class NotificationGate
    {
        private readonly Channel<int> _entered = Channel.CreateUnbounded<int>();
        private readonly Channel<int> _exited = Channel.CreateUnbounded<int>();
        private readonly Channel<NotificationResult> _results =
            Channel.CreateUnbounded<NotificationResult>();
        private int _notificationCount;

        public async ValueTask ObserveAsync(
            EventNotification<AppEventKind> notification,
            CancellationToken cancellationToken
        )
        {
            var count = ++_notificationCount;
            _entered.Writer.TryWrite(count);
            var result = await _results.Reader.ReadAsync(cancellationToken);
            _exited.Writer.TryWrite(count);
            if (result is NotificationResult.Failure)
            {
                throw new InvalidOperationException("runtime unavailable");
            }
        }

        public ValueTask<int> WaitForEntryAsync()
        {
            return _entered.Reader.ReadAsync();
        }

        public async ValueTask WaitForExitAsync(int expectedNotification)
        {
            while (await _exited.Reader.ReadAsync() != expectedNotification) { }
        }

        public void Release(NotificationResult result)
        {
            _results.Writer.TryWrite(result);
        }
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
