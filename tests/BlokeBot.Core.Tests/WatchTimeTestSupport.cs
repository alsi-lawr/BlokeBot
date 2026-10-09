using System.Net;
using System.Text;
using System.Threading.Channels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.WatchTime;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

internal sealed class WatchTimeTestSupport : IAsyncDisposable
{
    internal WatchTimeTestSupport(
        IDbContextFactory<BlokeBotDbContext> database,
        TimeProvider? clock = null
    )
    {
        Database = database;
        Clock = clock ?? new ManualTestTimeProvider(new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        Sessions = new(database, new(Events));
        var settings = BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions { ClientId = "client-id", BotUsername = "bot" },
            }
        );
        Runtime = new(
            database,
            Sessions,
            BotRuntime,
            settings,
            new(Tokens, Streams, new(Http, TwitchEndpointPolicy.Default), settings),
            new(database),
            new(Events),
            Clock,
            NullLogger<WatchTimeRuntime>.Instance
        );
        Runtime.StatusChanged += () =>
        {
            _ = _cycles.Writer.TryWrite(true);
            return Task.CompletedTask;
        };
    }

    internal IDbContextFactory<BlokeBotDbContext> Database { get; }
    internal TimeProvider Clock { get; }
    internal EventBus<AppEventKind> Events { get; } = TestEventBus.Create<AppEventKind>();
    internal HostedChannelRuntimeTransitionService Sessions { get; }
    internal WatchTimeRuntime Runtime { get; }
    internal WatchBotRuntime BotRuntime { get; } = new();
    internal WatchTokens Tokens { get; } = new();
    internal WatchStreams Streams { get; } = new();
    internal WatchHttp Http { get; } = new();
    private readonly Channel<bool> _cycles = Channel.CreateUnbounded<bool>();
    private bool _started;

    internal async Task<int> SeedAsync(bool enabled = true, string? amount = "7")
    {
        await using var db = await Database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            TwitchUserId = "100",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.Points,
            CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _ = db.PointsSettings.Add(new PointsSettings { HostId = host.Id });
        _ = await db.SaveChangesAsync();
        _ = await SaveAsync(host.Id, enabled, amount);
        return host.Id;
    }

    internal async Task<WatchTimeSettingsWriteResult> SaveAsync(
        int hostId,
        bool enabled,
        string? amount
    )
    {
        await using var db = await Database.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var result = await MainDatabaseStatements.ApplyWatchTimeSettingsAsync(
            db,
            hostId,
            enabled,
            amount,
            Guid.NewGuid(),
            Guid.NewGuid(),
            default
        );
        await tx.CommitAsync();
        Runtime.SettingsCommitted(hostId, result, Clock.GetUtcNow());
        return result;
    }

    internal async Task<BotChannelTarget> ConnectAsync(int hostId)
    {
        _ = await Sessions.RequestStartAsync(hostId, default);
        var target = await Sessions.GetOrCreateSessionTargetAsync(hostId, "streamer", default);
        (
            await Sessions.ConfirmStartedAsync("streamer", target.SessionIdentity, default)
        ).ShouldBeTrue();
        BotRuntime.Set(new BotRuntimeStatus.Connected(["streamer"]));
        Runtime.AcceptedStarted(target);
        return target;
    }

    internal async Task StartAsync()
    {
        await Runtime.StartAsync(default);
        _started = true;
    }

    internal async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await predicate())
        {
            _ = await _cycles.Reader.ReadAsync(timeout.Token);
        }
    }

    internal Task WaitStatusAsync(
        int hostId,
        WatchTimeStatusKind kind,
        DateTimeOffset? due = null
    ) =>
        WaitUntilAsync(() =>
            Task.FromResult(
                Runtime.GetStatus(hostId) is { } status
                    && status.Kind == kind
                    && (due is null || status.NextDue == due)
            )
        );

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await Runtime.StopAsync(default);
        }
        Runtime.Dispose();
        Sessions.Dispose();
    }
}

internal sealed class WatchBotRuntime : IBotRuntimeStatusAccessor
{
    public event Action? Changed;
    public BotRuntimeStatus Current { get; private set; } = new BotRuntimeStatus.Unauthorized();

    internal void Set(BotRuntimeStatus value)
    {
        Current = value;
        Changed?.Invoke();
    }
}

internal sealed class WatchTokens : IHostBotAccountTokenStatusProvider
{
    internal ActiveBotAccountTokenStatus Status { get; set; } = Ready("200", "bot");
    internal List<string[]> Required { get; } = [];

    internal static ActiveBotAccountTokenStatus Ready(string id, string login) =>
        new()
        {
            BotLogin = login,
            Status = new TokenStatus.Ready(
                "fixture-token",
                new TokenValidation(
                    id,
                    login,
                    OAuthScopeSet.Create([Scopes.ModeratorReadChatters])
                ),
                [Scopes.ModeratorReadChatters],
                [Scopes.ModeratorReadChatters]
            ),
        };

    public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
        string channelLogin,
        IEnumerable<string?> requiredScopes,
        CancellationToken cancellationToken
    )
    {
        Required.Add(requiredScopes.OfType<string>().ToArray());
        return Task.FromResult(Status);
    }
}

internal sealed class WatchStreams : IHostStreamLivenessProvider
{
    internal HostStreamLivenessOutcome Current { get; set; } =
        new HostStreamLivenessOutcome.Live("stream-1", new(2026, 10, 6, 11, 0, 0, TimeSpan.Zero));

    public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
        IO<HostStreamLivenessOutcome, Never>.Create(_ =>
            ValueTask.FromResult(Result<HostStreamLivenessOutcome, Never>.Success(Current))
        );
}

internal sealed class WatchHttp : IHttpClientFactory
{
    internal int Requests { get; private set; }
    internal Func<int, CancellationToken, Task<HttpResponseMessage>> Response { get; set; } =
        (request, _) =>
            Task.FromResult(
                Json(
                    request % 2 == 1
                        ? """{"data":[{"user_id":"200","user_login":"bot","user_name":"Bot"},{"user_id":"100","user_login":"streamer","user_name":"Streamer"}],"pagination":{"cursor":"page2"}}"""
                        : """{"data":[{"user_id":"300","user_login":"just_joined","user_name":"Just joined"},{"user_id":"400","user_login":"another_bot","user_name":"Another bot"}],"pagination":{}}"""
                )
            );

    internal static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    public HttpClient CreateClient(string name) => new(new Handler(this));

    private sealed class Handler(WatchHttp owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/chat/chatters");
            request.Headers.GetValues("Client-Id").Single().ShouldBe("client-id");
            request.Headers.Authorization!.Parameter.ShouldBe("fixture-token");
            return owner.Response(++owner.Requests, cancellationToken);
        }
    }
}
