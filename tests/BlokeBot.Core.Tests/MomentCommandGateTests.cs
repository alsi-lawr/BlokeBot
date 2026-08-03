using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MomentCommandGateTests
{
    [Test]
    public async Task DisabledMoments_SilenceBothOwnedCommandsBeforeLivenessAndProviderWork()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.None,
                    Login = "streamer",
                    DisplayName = "Streamer",
                    TwitchUserId = "streamer-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var liveness = new RecordingLivenessProvider();
        var providerOperations = new RecordingMomentProvider();
        var moments = new MomentHubService(
            database,
            providerOperations,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        var services = new ServiceCollection();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = services.AddSingleton<IHostStreamLivenessProvider>(liveness);
        _ = services.AddSingleton(moments);
        _ = services.AddChatCommands().AddCommandModule<MomentCommandModule>();
        await using var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<ChatCommandDispatcher>();
        var responses = new List<string>();

        await DispatchAsync(dispatcher, "!moment Great play", responses);
        await DispatchAsync(dispatcher, "!clip Great play", responses);

        responses.ShouldBeEmpty();
        liveness.Calls.ShouldBe(0);
        providerOperations.Calls.ShouldBe(0);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentCandidates.CountAsync()).ShouldBe(0);
        (await verify.MomentCaptureRequests.CountAsync()).ShouldBe(0);
    }

    private static async Task DispatchAsync(
        ChatCommandDispatcher dispatcher,
        string text,
        List<string> responses
    ) =>
        await dispatcher.DispatchResponsesAsync(
            new ChatMessage(
                "viewer",
                "streamer",
                text,
                $":viewer!u@h PRIVMSG #streamer :{text}",
                new Dictionary<string, string>()
            ),
            (response, _) =>
            {
                responses.Add(response.Message);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

    private sealed class RecordingLivenessProvider : IHostStreamLivenessProvider
    {
        internal int Calls { get; private set; }

        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin)
        {
            Calls++;
            return IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(
                    Result<HostStreamLivenessOutcome, Never>.Success(
                        new HostStreamLivenessOutcome.Live("stream-id")
                    )
                )
            );
        }
    }

    private sealed class RecordingMomentProvider : IMomentProviderOperations
    {
        internal int Calls { get; private set; }

        public Task<MomentProviderOutcome> CaptureAsync(
            int hostId,
            Guid publicId,
            bool markerFallbackEnabled,
            string description,
            CancellationToken ct
        )
        {
            Calls++;
            return Task.FromResult<MomentProviderOutcome>(
                new MomentProviderOutcome.Failed(null, null, "Unexpected provider call.")
            );
        }
    }
}
