using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public abstract partial class PointsGiveawaySchedulerTestBase
{
    private protected sealed class ThrowingGiveawayChangeNotification(string failureMessage)
        : IPointsGiveawayChangeNotification
    {
        public int Attempts { get; private set; }

        public ValueTask NotifyAsync(int hostId, CancellationToken cancellationToken)
        {
            Attempts++;
            return ValueTask.FromException(new IOException(failureMessage));
        }
    }

    private protected sealed class RecordingSchedulerNotification
        : IPointsGiveawaySchedulerNotification
    {
        public List<string> Messages { get; } = [];

        public ValueTask SendAsync(
            PointsGiveawaySchedule schedule,
            string message,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private protected sealed class ScriptedPublicChatSender(PublicChatSendOutcome outcome)
        : IPublicChatMessageSender
    {
        internal List<string> Messages { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return ValueTask.FromResult(outcome);
        }
    }

    private protected sealed class ThrowingSchedulerNotification(string failureMessage)
        : IPointsGiveawaySchedulerNotification
    {
        public ValueTask SendAsync(
            PointsGiveawaySchedule schedule,
            string message,
            CancellationToken cancellationToken
        ) => ValueTask.FromException(new HttpRequestException(failureMessage));
    }

    private protected class StaticTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        protected DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public override long GetTimestamp() => UtcNow.UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private protected sealed class AutoAdvanceTimeProvider(DateTimeOffset utcNow)
        : StaticTimeProvider(utcNow)
    {
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            if (dueTime > TimeSpan.Zero)
            {
                UtcNow = UtcNow.Add(dueTime);
            }

            callback(state);
            return CompletedTimer.Instance;
        }
    }

    private protected sealed class CompletedTimer : ITimer
    {
        internal static CompletedTimer Instance { get; } = new();

        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private protected sealed class RecordingGiveawayScheduler : IPointsGiveawayScheduler
    {
        public List<int> Cancelled { get; } = [];

        public List<PointsGiveawaySchedule> Scheduled { get; } = [];

        public void Schedule(PointsGiveawaySchedule schedule) => Scheduled.Add(schedule);

        public void Cancel(int giveawayId) => Cancelled.Add(giveawayId);
    }

    private protected sealed class FixedPointsRandom : IPointsRandom
    {
        public double NextDouble() => 0;

        public int Next(int minValue, int maxValue) => minValue;
    }

    private protected sealed class FakeHttpClientFactory(bool streamIsLive = false)
        : IHttpClientFactory
    {
        private readonly Handler _handler = new(streamIsLive);

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

        private sealed class Handler(bool streamIsLive) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            streamIsLive
                                ? """
                                {
                                  "data": [
                                    {
                                      "id": "stream-id",
                                      "user_id": "user-id",
                                      "user_login": "streamer",
                                      "user_name": "Streamer",
                                      "game_id": "game-id",
                                      "game_name": "Example Game",
                                      "type": "live",
                                      "title": "Representative stream",
                                      "tags": [],
                                      "viewer_count": 42,
                                      "started_at": "2026-07-13T12:34:56Z",
                                      "language": "en",
                                      "thumbnail_url": "https://example.test/{width}x{height}.jpg",
                                      "is_mature": false
                                    }
                                  ],
                                  "pagination": {}
                                }
                                """
                                : """{"data":[],"pagination":{}}""",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
        }
    }

    private protected sealed class StaticHostBotAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("app-token");
    }

    private protected sealed class ThrowingHostBotAppAccessTokenSource(Exception failure)
        : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            throw failure;
    }

    private protected sealed class UnavailableHostBotAccountTokenStatusProvider
        : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new ActiveBotAccountTokenStatus
                {
                    BotLogin = string.Empty,
                    Status = new TokenStatus.Unavailable(
                        AccessTokenUnavailableReason.MissingRefreshToken,
                        []
                    ),
                }
            );
    }

    private protected sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullLoggerScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private protected sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private protected sealed class NullLoggerScope : IDisposable
    {
        public static readonly NullLoggerScope Instance = new();

        public void Dispose() { }
    }
}
