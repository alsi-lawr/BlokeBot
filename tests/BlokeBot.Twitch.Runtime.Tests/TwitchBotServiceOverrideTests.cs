using BlokeBot.Commands;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchBotServiceOverrideTests
{
    [Test]
    public async Task NoOverrides_UsingRuntimeContracts_UsesCompleteDefaultBehavior()
    {
        var tokens = new RecordingAccessTokenProvider();
        var chat = new RecordingChatMessageSender();
        var services = CreateServices(tokens, chat);
        _ = services.AddTwitchBot(ConfigureBot, ValidPolicies());
        using var provider = services.BuildServiceProvider();

        var account = await provider
            .GetRequiredService<ITwitchBotAccountProvider>()
            .GetBotAccountAsync("streamer", CancellationToken.None);
        await provider
            .GetRequiredService<ITwitchCommandResponseSender>()
            .SendAsync(
                SourceMessage(),
                TwitchCommandResponse.Chat("default response"),
                CancellationToken.None
            );
        var lifecycle = provider.GetRequiredService<ITwitchBotChannelLifecycleNotifier>();
        await lifecycle.ChannelStartedAsync("streamer", CancellationToken.None);
        await lifecycle.ChannelStoppedAsync("streamer", CancellationToken.None);

        account.ShouldBe(new TwitchBotAccount("mainbot", "default-token"));
        tokens.CallCount.ShouldBe(1);
        var sent = chat.Messages.ShouldHaveSingleItem();
        sent.Channel.ShouldBe("streamer");
        sent.Message.ShouldBe("default response");
        sent.Deadline.ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
    }

    [Test]
    public async Task ExplicitOverrides_UsingRuntimeContracts_UsesFeatureSingletonBehavior()
    {
        var defaultTokens = new RecordingAccessTokenProvider();
        var defaultChat = new RecordingChatMessageSender();
        var accountProvider = new FeatureAccountProvider();
        var responseSender = new FeatureResponseSender();
        var lifecycleNotifier = new FeatureLifecycleNotifier();
        var services = CreateServices(defaultTokens, defaultChat);
        services.AddSingleton(accountProvider);
        services.AddSingleton(responseSender);
        services.AddSingleton(lifecycleNotifier);
        _ = services
            .AddTwitchBot(ConfigureBot, ValidPolicies())
            .OverrideAccountProviderWith<FeatureAccountProvider>()
            .OverrideCommandResponseSenderWith<FeatureResponseSender>()
            .OverrideChannelLifecycleNotifierWith<FeatureLifecycleNotifier>();
        using var provider = services.BuildServiceProvider();

        var account = await provider
            .GetRequiredService<ITwitchBotAccountProvider>()
            .GetBotAccountAsync("streamer", CancellationToken.None);
        await provider
            .GetRequiredService<ITwitchCommandResponseSender>()
            .SendAsync(
                SourceMessage(),
                TwitchCommandResponse.Whisper("feature response"),
                CancellationToken.None
            );
        var lifecycle = provider.GetRequiredService<ITwitchBotChannelLifecycleNotifier>();
        await lifecycle.ChannelStartedAsync("streamer", CancellationToken.None);
        await lifecycle.ChannelStoppedAsync("streamer", CancellationToken.None);

        account.ShouldBe(new TwitchBotAccount("feature-bot", "feature-token"));
        accountProvider.Channels.ShouldBe(["streamer"]);
        responseSender.Responses.ShouldBe([
            new RecordedResponse(
                "streamer",
                TwitchCommandResponseTarget.Whisper,
                "feature response"
            ),
        ]);
        lifecycleNotifier.StartedChannels.ShouldBe(["streamer"]);
        lifecycleNotifier.StoppedChannels.ShouldBe(["streamer"]);
        defaultTokens.CallCount.ShouldBe(0);
        defaultChat.Messages.ShouldBeEmpty();
    }

    private static ServiceCollection CreateServices(
        RecordingAccessTokenProvider tokens,
        RecordingChatMessageSender chat
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITwitchAccessTokenProvider>(tokens);
        services.AddSingleton<ITwitchChatMessageSender>(chat);
        services.AddSingleton<ILogger<TwitchChatCommandResponseSender>>(
            NullLogger<TwitchChatCommandResponseSender>.Instance
        );
        return services;
    }

    private static void ConfigureBot(TwitchBotOptions options)
    {
        options.Identity = new TwitchBotIdentityOptions
        {
            BotUsername = "MainBot",
            ClientId = "client-id",
            ClientSecret = "private-client-secret",
            RedirectUri = "https://localhost/callback",
            Scopes = ["chat:read"],
            TokenCachePath = "private-token-cache.json",
        };
    }

    private static TwitchBotPolicyOptions ValidPolicies()
    {
        var delay = TimeSpan.FromSeconds(1);
        return new()
        {
            IrcSession = new IrcSessionResilienceOptions
            {
                AttemptLimit = 1,
                Delay = delay,
                MaximumDelay = delay,
                DelayBackoffType = DelayBackoffType.Constant,
                AttemptTimeout = TimeSpan.FromMinutes(1),
            },
            EventSubSession = new EventSubSessionResilienceOptions
            {
                AttemptLimit = 1,
                Delay = delay,
                MaximumDelay = delay,
                DelayBackoffType = DelayBackoffType.Constant,
                AttemptTimeout = TimeSpan.FromMinutes(1),
            },
            EventSubChannelRecovery = new EventSubChannelRecoveryOptions
            {
                AttemptLimit = 1,
                Delay = delay,
                MaximumDelay = delay,
                DelayBackoffType = DelayBackoffType.Constant,
                AttemptTimeout = TimeSpan.FromMinutes(1),
            },
            PublicChatRetry = new PublicChatRetryOptions
            {
                AttemptLimit = 1,
                Delay = delay,
                MaximumDelay = delay,
                DelayBackoffType = DelayBackoffType.Constant,
            },
            PublicChatDeliveryLifetime = new PublicChatDeliveryLifetimeOptions
            {
                MaximumAge = TimeSpan.FromSeconds(30),
            },
            PublicChatTerminalRetention = new PublicChatTerminalRetentionOptions
            {
                Duration = TimeSpan.FromDays(1),
            },
        };
    }

    private static TwitchChatMessage SourceMessage()
    {
        return new("viewer", "streamer", "!command", "raw", new Dictionary<string, string>());
    }

    private sealed class RecordingAccessTokenProvider : ITwitchAccessTokenProvider
    {
        internal int CallCount { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult("default-token");
        }
    }

    private sealed class RecordingChatMessageSender : ITwitchChatMessageSender
    {
        internal List<SentMessage> Messages { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(new(channel, message, deadline));
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Accepted()
            );
        }
    }

    private sealed class FeatureAccountProvider : ITwitchBotAccountProvider
    {
        internal List<string> Channels { get; } = [];

        public ValueTask<TwitchBotAccount> GetBotAccountAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Channels.Add(channelLogin);
            return ValueTask.FromResult(new TwitchBotAccount("feature-bot", "feature-token"));
        }
    }

    private sealed class FeatureResponseSender : ITwitchCommandResponseSender
    {
        internal List<RecordedResponse> Responses { get; } = [];

        public ValueTask SendAsync(
            TwitchChatMessage sourceMessage,
            TwitchCommandResponse response,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Responses.Add(new(sourceMessage.Channel, response.Target, response.Message));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FeatureLifecycleNotifier : ITwitchBotChannelLifecycleNotifier
    {
        internal List<string> StartedChannels { get; } = [];

        internal List<string> StoppedChannels { get; } = [];

        public Task ChannelStartedAsync(string channel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartedChannels.Add(channel);
            return Task.CompletedTask;
        }

        public Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoppedChannels.Add(channel);
            return Task.CompletedTask;
        }
    }

    private sealed record SentMessage(
        string Channel,
        string Message,
        PublicChatDeliveryDeadline Deadline
    );

    private sealed record RecordedResponse(
        string Channel,
        TwitchCommandResponseTarget Target,
        string Message
    );
}
