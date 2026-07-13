using BlokeBot.Commands;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Configuration;
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
            .GetRequiredService<ICommandResponseSender>()
            .SendAsync(
                SourceMessage(),
                CommandResponse.Chat("default response"),
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
            .GetRequiredService<ICommandResponseSender>()
            .SendAsync(
                SourceMessage(),
                CommandResponse.Whisper("feature response"),
                CancellationToken.None
            );
        var lifecycle = provider.GetRequiredService<ITwitchBotChannelLifecycleNotifier>();
        await lifecycle.ChannelStartedAsync("streamer", CancellationToken.None);
        await lifecycle.ChannelStoppedAsync("streamer", CancellationToken.None);

        account.ShouldBe(new TwitchBotAccount("feature-bot", "feature-token"));
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
        services.AddSingleton(firstAccountProvider);
        services.AddSingleton(firstResponseSender);
        services.AddSingleton(firstLifecycleNotifier);
        services.AddSingleton<ITwitchBotAccountProvider>(firstAccountProvider);
        services.AddSingleton<ICommandResponseSender>(firstResponseSender);
        services.AddSingleton<ITwitchBotChannelLifecycleNotifier>(firstLifecycleNotifier);
        services.AddSingleton(accountProvider);
        services.AddSingleton(responseSender);
        services.AddSingleton(lifecycleNotifier);
        _ = services
            .AddTwitchBot(ValidConfiguration())
            .OverrideAccountProviderWith<FirstFeatureAccountProvider>()
            .OverrideAccountProviderWith<FeatureAccountProvider>()
            .OverrideCommandResponseSenderWith<FirstFeatureResponseSender>()
            .OverrideCommandResponseSenderWith<FeatureResponseSender>()
            .OverrideChannelLifecycleNotifierWith<FirstFeatureLifecycleNotifier>()
            .OverrideChannelLifecycleNotifierWith<FeatureLifecycleNotifier>();
        using var provider = services.BuildServiceProvider();

        var accountContract = provider
            .GetServices<ITwitchBotAccountProvider>()
            .ShouldHaveSingleItem();
        var responseContract = provider
            .GetServices<ICommandResponseSender>()
            .ShouldHaveSingleItem();
        var lifecycleContract = provider
            .GetServices<ITwitchBotChannelLifecycleNotifier>()
            .ShouldHaveSingleItem();
        accountContract.ShouldBeSameAs(accountProvider);
        responseContract.ShouldBeSameAs(responseSender);
        lifecycleContract.ShouldBeSameAs(lifecycleNotifier);

        var account = await accountContract.GetBotAccountAsync(
            "configured-streamer",
            CancellationToken.None
        );
        await responseContract.SendAsync(
            SourceMessage(),
            CommandResponse.Whisper("last response"),
            CancellationToken.None
        );
        await lifecycleContract.ChannelStartedAsync("configured-streamer", CancellationToken.None);
        await lifecycleContract.ChannelStoppedAsync("configured-streamer", CancellationToken.None);

        account.ShouldBe(new TwitchBotAccount("feature-bot", "feature-token"));
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
        services.AddSingleton<IAccessTokenProvider>(tokens);
        services.AddSingleton<IPublicChatMessageSender>(chat);
        services.AddSingleton<ILogger<PublicChatCommandResponseSender>>(
            NullLogger<PublicChatCommandResponseSender>.Instance
        );
        return services;
    }

    private static void ConfigureBot(TwitchBotOptions options)
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
            ["TwitchBot:Policies:IrcSession:AttemptLimit"] = "1",
            ["TwitchBot:Policies:IrcSession:Delay"] = "00:00:01",
            ["TwitchBot:Policies:IrcSession:MaximumDelay"] = "00:00:01",
            ["TwitchBot:Policies:IrcSession:DelayBackoffType"] = "Constant",
            ["TwitchBot:Policies:IrcSession:AttemptTimeout"] = "00:01:00",
            ["TwitchBot:Policies:EventSubSession:AttemptLimit"] = "1",
            ["TwitchBot:Policies:EventSubSession:Delay"] = "00:00:01",
            ["TwitchBot:Policies:EventSubSession:MaximumDelay"] = "00:00:01",
            ["TwitchBot:Policies:EventSubSession:DelayBackoffType"] = "Constant",
            ["TwitchBot:Policies:EventSubSession:AttemptTimeout"] = "00:01:00",
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

    private static ChatMessage SourceMessage()
    {
        return new("viewer", "streamer", "!command", "raw", new Dictionary<string, string>());
    }

    private sealed class RecordingAccessTokenProvider : IAccessTokenProvider
    {
        internal int CallCount { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult("default-token");
        }
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

    private sealed class FirstFeatureAccountProvider : ITwitchBotAccountProvider
    {
        public ValueTask<TwitchBotAccount> GetBotAccountAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new TwitchBotAccount("first-bot", "first-token"));
        }
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

    private sealed class FirstFeatureLifecycleNotifier : ITwitchBotChannelLifecycleNotifier
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
