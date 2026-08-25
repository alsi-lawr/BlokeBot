using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class BotPolicyTests
{
    [Test]
    public void PublicChatLifetime_Validating_RequiresPositiveAtMostSixtySeconds()
    {
        ValidateLifetime(null).Failed.ShouldBeTrue();
        ValidateLifetime(TimeSpan.Zero).Failed.ShouldBeTrue();
        ValidateLifetime(TimeSpan.FromTicks(-1)).Failed.ShouldBeTrue();
        ValidateLifetime(TimeSpan.FromSeconds(60).Add(TimeSpan.FromTicks(1))).Failed.ShouldBeTrue();
        ValidateLifetime(TimeSpan.FromSeconds(60)).Failed.ShouldBeFalse();
    }

    [Test]
    public void RequiredPolicySectionMissing_Binding_FailsWithBoundaryName()
    {
        const string SectionName = "IrcSession";
        var values = ValidConfiguration()
            .Where(pair =>
                !pair.Key.StartsWith($"TwitchBot:Policies:{SectionName}:", StringComparison.Ordinal)
            )
            .ToDictionary();
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection("TwitchBot");

        var exception = Should.Throw<OptionsValidationException>(() =>
            BotPolicies.BindRequired(section)
        );

        exception.OptionsName.ShouldContain(SectionName);
        exception.Message.ShouldNotContain("secret", Case.Insensitive);
        exception.Message.ShouldNotContain("message content", Case.Insensitive);
    }

    [Test]
    public void MaximumDelayBeforeDelay_Mapping_FailsExplicitCrossFieldRule()
    {
        var options = ValidOptions();
        options.EventSubChannelRecovery.MaximumDelay =
            options.EventSubChannelRecovery.Delay - TimeSpan.FromTicks(1);

        var exception = Should.Throw<OptionsValidationException>(() =>
            BotPolicies.FromOptions(options)
        );

        exception.OptionsName.ShouldContain(nameof(BotPolicyOptions.EventSubChannelRecovery));
    }

    [Test]
    public void MutablePolicyOptions_Mapping_IsolatesSnapshotFromLaterMutation()
    {
        var options = ValidOptions();
        var policies = BotPolicies.FromOptions(options);

        options.IrcSession.AttemptLimit = 99;
        options.IrcSession.Delay = TimeSpan.FromHours(1);
        options.PublicChatTerminalRetention.Duration = TimeSpan.FromDays(99);
        options.PublicChatDeliveryLifetime.MaximumAge = TimeSpan.FromSeconds(1);

        policies.IrcSession.AttemptLimit.ShouldBe(5);
        policies.IrcSession.Delay.ShouldBe(TimeSpan.FromSeconds(3));
        policies.PublicChatTerminalRetention.Duration.ShouldBe(TimeSpan.FromDays(7));
        policies.PublicChatDeliveryLifetime.MaximumAge.ShouldBe(TimeSpan.FromSeconds(30));
    }

    private static BotPolicyOptions ValidOptions() =>
        new()
        {
            IrcSession = new IrcSessionResilienceOptions
            {
                AttemptLimit = 5,
                Delay = TimeSpan.FromSeconds(3),
                MaximumDelay = TimeSpan.FromSeconds(30),
                DelayBackoffType = DelayBackoffType.Exponential,
                AttemptTimeout = TimeSpan.FromMinutes(2),
            },
            EventSubChannelRecovery = new EventSubChannelRecoveryOptions
            {
                AttemptLimit = 5,
                Delay = TimeSpan.FromSeconds(5),
                MaximumDelay = TimeSpan.FromMinutes(1),
                DelayBackoffType = DelayBackoffType.Exponential,
                AttemptTimeout = TimeSpan.FromMinutes(1),
            },
            PublicChatRetry = new PublicChatRetryOptions
            {
                AttemptLimit = 3,
                Delay = TimeSpan.FromSeconds(1),
                MaximumDelay = TimeSpan.FromSeconds(30),
                DelayBackoffType = DelayBackoffType.Exponential,
            },
            PublicChatDeliveryLifetime = new PublicChatDeliveryLifetimeOptions
            {
                MaximumAge = TimeSpan.FromSeconds(30),
            },
            PublicChatTerminalRetention = new PublicChatTerminalRetentionOptions
            {
                Duration = TimeSpan.FromDays(7),
            },
        };

    private static Dictionary<string, string?> ValidConfiguration() =>
        new()
        {
            ["TwitchBot:Policies:IrcSession:AttemptLimit"] = "5",
            ["TwitchBot:Policies:IrcSession:Delay"] = "00:00:03",
            ["TwitchBot:Policies:IrcSession:MaximumDelay"] = "00:00:30",
            ["TwitchBot:Policies:IrcSession:DelayBackoffType"] = "Exponential",
            ["TwitchBot:Policies:IrcSession:AttemptTimeout"] = "00:02:00",
            ["TwitchBot:Policies:EventSubChannelRecovery:AttemptLimit"] = "5",
            ["TwitchBot:Policies:EventSubChannelRecovery:Delay"] = "00:00:05",
            ["TwitchBot:Policies:EventSubChannelRecovery:MaximumDelay"] = "00:01:00",
            ["TwitchBot:Policies:EventSubChannelRecovery:DelayBackoffType"] = "Exponential",
            ["TwitchBot:Policies:EventSubChannelRecovery:AttemptTimeout"] = "00:01:00",
            ["TwitchBot:Policies:PublicChatRetry:AttemptLimit"] = "3",
            ["TwitchBot:Policies:PublicChatRetry:Delay"] = "00:00:01",
            ["TwitchBot:Policies:PublicChatRetry:MaximumDelay"] = "00:00:30",
            ["TwitchBot:Policies:PublicChatRetry:DelayBackoffType"] = "Exponential",
            ["TwitchBot:Policies:PublicChatDeliveryLifetime:MaximumAge"] = "00:00:30",
            ["TwitchBot:Policies:PublicChatTerminalRetention:Duration"] = "7.00:00:00",
        };

    private static ValidateOptionsResult ValidateLifetime(TimeSpan? maximumAge) =>
        new PublicChatDeliveryLifetimeOptionsValidator().Validate(
            "public chat lifetime",
            new PublicChatDeliveryLifetimeOptions { MaximumAge = maximumAge }
        );
}
