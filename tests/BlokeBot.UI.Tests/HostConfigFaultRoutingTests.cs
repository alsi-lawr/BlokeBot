using BlokeBot.Components;
using BlokeBot.Features.AccessLists;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.HostConfig.Page;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Hosting;
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

namespace BlokeBot.UI.Tests;

public sealed class HostConfigFaultRoutingTests
{
    [Test]
    public async Task AddAccess_Faulting_RedactsTelemetryAndReachesErrorBoundary()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var faultingDbFactory = new FaultingDbContextFactory(dbFactory);
        var logger = new RecordingLogger<UiFaultTelemetry>();
        ConfigureHostServices(context, faultingDbFactory, logger);
        context.ComponentFactories.AddStub<HostBotChannelStatusPanel>();
        RenderFragment content = builder =>
        {
            builder.OpenComponent<HostConfigPage>(0);
            builder.CloseComponent();
        };
        var boundary = context.Render<UiFaultRoutingTests.CapturingErrorBoundary>(parameters =>
            parameters.Add(x => x.ChildContent, content)
        );
        var input = boundary
            .FindAll("input[placeholder='mod username']")
            .Single(element => element.GetAttribute("disabled") is null);
        input.Input("moderator");
        const string SensitiveMessage = "secret-host-config-failure";
        var exception = new InvalidOperationException(SensitiveMessage);
        faultingDbFactory.Failure = exception;

        boundary
            .FindAll("button")
            .Single(element =>
                element.TextContent.Trim() == "Add" && element.GetAttribute("disabled") is null
            )
            .Click();

        boundary.WaitForAssertion(() =>
            boundary.Instance.CapturedException.ShouldBeSameAs(exception)
        );
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Properties["UiComponent"].ShouldBe(nameof(HostConfigPage));
        entry.Properties["UiOperation"].ShouldBe("AddAccessAsync");
        entry.Properties["HostId"].ShouldBe(hostId);
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
        entry.Message.ShouldNotContain(SensitiveMessage);
    }

    private static void ConfigureHostServices(
        BunitContext context,
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        ILogger<UiFaultTelemetry> logger
    )
    {
        context.Services.AddSingleton(dbFactory);
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

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
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

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new(exception, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        Exception? Exception,
        string Message,
        IReadOnlyDictionary<string, object?> Properties
    );
}
