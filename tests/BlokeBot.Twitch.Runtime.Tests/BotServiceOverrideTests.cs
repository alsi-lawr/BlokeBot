using BlokeBot.Commands;
using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class BotServiceOverrideTests
{
    [Test]
    public async Task NoOverrides_UsingRuntimeContracts_UsesCompleteDefaultBehavior()
    {
        var tokens = new RecordingAccessTokenProvider();
        var chat = new RecordingChatMessageSender();
        var services = CreateServices(tokens, chat);
        _ = services.AddTwitchBot(ConfigureBot, ValidPolicies(), online: false);
        using var provider = services.BuildServiceProvider();

        var account = await provider
            .GetRequiredService<IBotAccountProvider>()
            .GetBotAccount("streamer")
            .ExecuteAsync(CancellationToken.None);
        await provider
            .GetRequiredService<ICommandResponseSender>()
            .SendAsync(
                SourceMessage(),
                CommandResponse.Chat("default response"),
                CancellationToken.None
            );
        var lifecycle = provider.GetRequiredService<IBotChannelLifecycleNotifier>();
        await lifecycle.ChannelStartedAsync("streamer", CancellationToken.None);
        await lifecycle.ChannelStoppedAsync("streamer", CancellationToken.None);

        Success(account).ShouldBe(new BotAccount("mainbot", "default-token"));
        tokens.CallCount.ShouldBe(1);
        var sent = chat.Messages.ShouldHaveSingleItem();
        sent.Channel.ShouldBe("streamer");
        sent.Message.ShouldBe("default response");
        _ = sent.Deadline.ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
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
        _ = services.AddSingleton(accountProvider);
        _ = services.AddSingleton(responseSender);
        _ = services.AddSingleton(lifecycleNotifier);
        _ = services
            .AddTwitchBot(ConfigureBot, ValidPolicies(), online: false)
            .OverrideAccountProviderWith<FeatureAccountProvider>()
            .OverrideCommandResponseSenderWith<FeatureResponseSender>()
            .OverrideChannelLifecycleNotifierWith<FeatureLifecycleNotifier>();
        using var provider = services.BuildServiceProvider();

        var account = await provider
            .GetRequiredService<IBotAccountProvider>()
            .GetBotAccount("streamer")
            .ExecuteAsync(CancellationToken.None);
        await provider
            .GetRequiredService<ICommandResponseSender>()
            .SendAsync(
                SourceMessage(),
                CommandResponse.Whisper("feature response"),
                CancellationToken.None
            );
        var lifecycle = provider.GetRequiredService<IBotChannelLifecycleNotifier>();
        await lifecycle.ChannelStartedAsync("streamer", CancellationToken.None);
        await lifecycle.ChannelStoppedAsync("streamer", CancellationToken.None);

        Success(account).ShouldBe(new BotAccount("feature-bot", "feature-token"));
        accountProvider.Channels.ShouldBe(["streamer"]);
        responseSender.Responses.ShouldBe([
            new RecordedResponse("streamer", CommandResponseTarget.Whisper, "feature response"),
        ]);
        lifecycleNotifier.StartedChannels.ShouldBe(["streamer"]);
        lifecycleNotifier.StoppedChannels.ShouldBe(["streamer"]);
        defaultTokens.CallCount.ShouldBe(0);
        defaultChat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task ConfigurationOverrides_RepeatedAfterPreRegisteredCompetitors_ExposeOnlyLastFeatureBehavior()
    {
        var defaultTokens = new RecordingAccessTokenProvider();
        var defaultChat = new RecordingChatMessageSender();
        var firstAccountProvider = new FirstFeatureAccountProvider();
        var firstResponseSender = new FirstFeatureResponseSender();
        var firstLifecycleNotifier = new FirstFeatureLifecycleNotifier();
        var accountProvider = new FeatureAccountProvider();
        var responseSender = new FeatureResponseSender();
        var lifecycleNotifier = new FeatureLifecycleNotifier();
        var services = CreateServices(defaultTokens, defaultChat);
        _ = services.AddSingleton(firstAccountProvider);
        _ = services.AddSingleton(firstResponseSender);
        _ = services.AddSingleton(firstLifecycleNotifier);
        _ = services.AddSingleton<IBotAccountProvider>(firstAccountProvider);
        _ = services.AddSingleton<ICommandResponseSender>(firstResponseSender);
        _ = services.AddSingleton<IBotChannelLifecycleNotifier>(firstLifecycleNotifier);
        _ = services.AddSingleton(accountProvider);
        _ = services.AddSingleton(responseSender);
        _ = services.AddSingleton(lifecycleNotifier);
        _ = services
            .AddTwitchBot(ValidConfiguration(), online: false)
            .OverrideAccountProviderWith<FirstFeatureAccountProvider>()
            .OverrideAccountProviderWith<FeatureAccountProvider>()
            .OverrideCommandResponseSenderWith<FirstFeatureResponseSender>()
            .OverrideCommandResponseSenderWith<FeatureResponseSender>()
            .OverrideChannelLifecycleNotifierWith<FirstFeatureLifecycleNotifier>()
            .OverrideChannelLifecycleNotifierWith<FeatureLifecycleNotifier>();
        using var provider = services.BuildServiceProvider();

        var accountContract = provider.GetServices<IBotAccountProvider>().ShouldHaveSingleItem();
        var responseContract = provider
            .GetServices<ICommandResponseSender>()
            .ShouldHaveSingleItem();
        var lifecycleContract = provider
            .GetServices<IBotChannelLifecycleNotifier>()
            .ShouldHaveSingleItem();
        accountContract.ShouldBeSameAs(accountProvider);
        responseContract.ShouldBeSameAs(responseSender);
        lifecycleContract.ShouldBeSameAs(lifecycleNotifier);

        var account = await accountContract
            .GetBotAccount("configured-streamer")
            .ExecuteAsync(CancellationToken.None);
        await responseContract.SendAsync(
            SourceMessage(),
            CommandResponse.Whisper("last response"),
            CancellationToken.None
        );
        await lifecycleContract.ChannelStartedAsync("configured-streamer", CancellationToken.None);
        await lifecycleContract.ChannelStoppedAsync("configured-streamer", CancellationToken.None);

        Success(account).ShouldBe(new BotAccount("feature-bot", "feature-token"));
        accountProvider.Channels.ShouldBe(["configured-streamer"]);
        responseSender.Responses.ShouldBe([
            new RecordedResponse("streamer", CommandResponseTarget.Whisper, "last response"),
        ]);
        lifecycleNotifier.StartedChannels.ShouldBe(["configured-streamer"]);
        lifecycleNotifier.StoppedChannels.ShouldBe(["configured-streamer"]);
        defaultTokens.CallCount.ShouldBe(0);
        defaultChat.Messages.ShouldBeEmpty();
    }

    private static ServiceCollection CreateServices(
        RecordingAccessTokenProvider tokens,
        RecordingChatMessageSender chat
    )
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton<IAccessTokenProvider>(tokens);
        _ = services.AddSingleton<IPublicChatMessageSender>(chat);
        _ = services.AddSingleton<ILogger<PublicChatCommandResponseSender>>(
            NullLogger<PublicChatCommandResponseSender>.Instance
        );
        return services;
    }

    private static void ConfigureBot(BotOptions options)
    {
        options.Identity = new BotIdentityOptions
        {
            BotUsername = "MainBot",
            ClientId = "client-id",
            ClientSecret = "private-client-secret",
            RedirectUri = "https://localhost/callback",
            Scopes = ["chat:read"],
            TokenCachePath = "private-token-cache.json",
        };
        options.EventSubWebhook = new EventSubWebhookOptions
        {
            CallbackUri = new Uri("http://127.0.0.1:5080/eventsub/twitch"),
            Secret = "runtime-test-secret",
        };
    }

    private static BotPolicyOptions ValidPolicies()
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

    private static IConfiguration ValidConfiguration()
    {
        var values = new Dictionary<string, string?>
        {
            ["TwitchBot:Identity:BotUsername"] = "MainBot",
            ["TwitchBot:Identity:ClientId"] = "client-id",
            ["TwitchBot:Identity:ClientSecret"] = "private-client-secret",
            ["TwitchBot:Identity:RedirectUri"] = "https://localhost/callback",
            ["TwitchBot:Identity:Scopes:0"] = "chat:read",
            ["TwitchBot:Identity:TokenCachePath"] = "private-token-cache.json",
            ["TwitchBot:EventSubWebhook:CallbackUri"] = "http://127.0.0.1:5080/eventsub/twitch",
            ["TwitchBot:EventSubWebhook:Secret"] = "runtime-test-secret",
            ["TwitchBot:Policies:IrcSession:AttemptLimit"] = "1",
            ["TwitchBot:Policies:IrcSession:Delay"] = "00:00:01",
            ["TwitchBot:Policies:IrcSession:MaximumDelay"] = "00:00:01",
            ["TwitchBot:Policies:IrcSession:DelayBackoffType"] = "Constant",
            ["TwitchBot:Policies:IrcSession:AttemptTimeout"] = "00:01:00",
            ["TwitchBot:Policies:EventSubChannelRecovery:AttemptLimit"] = "1",
            ["TwitchBot:Policies:EventSubChannelRecovery:Delay"] = "00:00:01",
            ["TwitchBot:Policies:EventSubChannelRecovery:MaximumDelay"] = "00:00:01",
            ["TwitchBot:Policies:EventSubChannelRecovery:DelayBackoffType"] = "Constant",
            ["TwitchBot:Policies:EventSubChannelRecovery:AttemptTimeout"] = "00:01:00",
            ["TwitchBot:Policies:PublicChatRetry:AttemptLimit"] = "1",
            ["TwitchBot:Policies:PublicChatRetry:Delay"] = "00:00:01",
            ["TwitchBot:Policies:PublicChatRetry:MaximumDelay"] = "00:00:01",
            ["TwitchBot:Policies:PublicChatRetry:DelayBackoffType"] = "Constant",
            ["TwitchBot:Policies:PublicChatDeliveryLifetime:MaximumAge"] = "00:00:30",
            ["TwitchBot:Policies:PublicChatTerminalRetention:Duration"] = "1.00:00:00",
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection("TwitchBot");
    }

    private static ChatMessage SourceMessage() =>
        new("viewer", "streamer", "!command", "raw", new Dictionary<string, string>());

    private static BotAccount Success(Result<BotAccount, AccessTokenUnavailableReason> result) =>
        result.Match(
            static account => account,
            static reason =>
                throw new InvalidOperationException($"Expected a bot account, received {reason}.")
        );

    private sealed class RecordingAccessTokenProvider : IAccessTokenProvider
    {
        internal int CallCount { get; private set; }

        public IO<string, AccessTokenUnavailableReason> GetAccessToken() =>
            IO<string, AccessTokenUnavailableReason>.Create(cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                return ValueTask.FromResult(
                    Result<string, AccessTokenUnavailableReason>.Success("default-token")
                );
            });
    }

    private sealed class RecordingChatMessageSender : IPublicChatMessageSender
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

    private sealed class FeatureAccountProvider : IBotAccountProvider
    {
        internal List<string> Channels { get; } = [];

        public IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string channelLogin) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Channels.Add(channelLogin);
                return ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        new BotAccount("feature-bot", "feature-token")
                    )
                );
            });
    }

    private sealed class FirstFeatureAccountProvider : IBotAccountProvider
    {
        public IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string channelLogin) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(static cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        new BotAccount("first-bot", "first-token")
                    )
                );
            });
    }

    private sealed class FeatureResponseSender : ICommandResponseSender
    {
        internal List<RecordedResponse> Responses { get; } = [];

        public ValueTask SendAsync(
            ChatMessage sourceMessage,
            CommandResponse response,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Responses.Add(new(sourceMessage.Channel, response.Target, response.Message));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FirstFeatureResponseSender : ICommandResponseSender
    {
        public ValueTask SendAsync(
            ChatMessage sourceMessage,
            CommandResponse response,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FeatureLifecycleNotifier : IBotChannelLifecycleNotifier
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

    private sealed class FirstFeatureLifecycleNotifier : IBotChannelLifecycleNotifier
    {
        public Task ChannelStartedAsync(string channel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        CommandResponseTarget Target,
        string Message
    );
}
