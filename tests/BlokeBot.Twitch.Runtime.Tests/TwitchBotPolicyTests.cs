using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchBotPolicyTests
{
    [Test]
    public void CompletePolicyOptions_Validating_AcceptsEveryBoundary()
    {
        var options = ValidOptions();

        new IrcSessionResilienceOptionsValidator()
            .Validate("IRC session", options.IrcSession)
            .Failed.ShouldBeFalse();
        new EventSubSessionResilienceOptionsValidator()
            .Validate("EventSub session", options.EventSubSession)
            .Failed.ShouldBeFalse();
        new EventSubChannelRecoveryOptionsValidator()
            .Validate("EventSub channel recovery", options.EventSubChannelRecovery)
            .Failed.ShouldBeFalse();
        new PublicChatRetryOptionsValidator()
            .Validate("public chat retry", options.PublicChatRetry)
            .Failed.ShouldBeFalse();
        new PublicChatDeliveryLifetimeOptionsValidator()
            .Validate("public chat lifetime", options.PublicChatDeliveryLifetime)
            .Failed.ShouldBeFalse();
        new PublicChatTerminalRetentionOptionsValidator()
            .Validate("public chat retention", options.PublicChatTerminalRetention)
            .Failed.ShouldBeFalse();
    }

    [Test]
    public void MissingIrcValues_Validating_ReportsEveryRequiredMember()
    {
        var result = new IrcSessionResilienceOptionsValidator().Validate(
            "IRC session",
            new IrcSessionResilienceOptions
            {
                AttemptLimit = null,
                Delay = null,
                MaximumDelay = null,
                DelayBackoffType = null,
                AttemptTimeout = null,
            }
        );

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains(nameof(IrcSessionResilienceOptions.AttemptLimit), StringComparison.Ordinal));
        result.Failures.ShouldContain(failure => failure.Contains(nameof(IrcSessionResilienceOptions.Delay), StringComparison.Ordinal));
        result.Failures.ShouldContain(failure => failure.Contains(nameof(IrcSessionResilienceOptions.MaximumDelay), StringComparison.Ordinal));
        result.Failures.ShouldContain(failure => failure.Contains(nameof(IrcSessionResilienceOptions.DelayBackoffType), StringComparison.Ordinal));
        result.Failures.ShouldContain(failure => failure.Contains(nameof(IrcSessionResilienceOptions.AttemptTimeout), StringComparison.Ordinal));
    }

    [Test]
    public void InvalidEventSubSessionValues_Validating_RejectsRangesAndEnum()
    {
        var result = new EventSubSessionResilienceOptionsValidator().Validate(
            "EventSub session",
            new EventSubSessionResilienceOptions
            {
                AttemptLimit = 0,
                Delay = TimeSpan.Zero,
                MaximumDelay = TimeSpan.Zero,
                DelayBackoffType = (DelayBackoffType)int.MaxValue,
                AttemptTimeout = TimeSpan.Zero,
            }
        );

        result.Failed.ShouldBeTrue();
        result.Failures.Count().ShouldBeGreaterThanOrEqualTo(5);
    }

    [Test]
    public void InvalidEventSubChannelRecoveryValues_Validating_RejectsRanges()
    {
        var result = new EventSubChannelRecoveryOptionsValidator().Validate(
            "EventSub channel recovery",
            new EventSubChannelRecoveryOptions
            {
                AttemptLimit = -1,
                Delay = TimeSpan.Zero,
                MaximumDelay = TimeSpan.Zero,
                DelayBackoffType = DelayBackoffType.Linear,
                AttemptTimeout = TimeSpan.Zero,
            }
        );

        result.Failed.ShouldBeTrue();
        result.Failures.Count().ShouldBeGreaterThanOrEqualTo(4);
    }

    [Test]
    public void InvalidPublicChatValues_Validating_RejectsRetryAndRetentionRanges()
    {
        var retry = new PublicChatRetryOptionsValidator().Validate(
            "public chat retry",
            new PublicChatRetryOptions
            {
                AttemptLimit = 0,
                Delay = TimeSpan.Zero,
                MaximumDelay = TimeSpan.Zero,
                DelayBackoffType = DelayBackoffType.Constant,
            }
        );
        var retention = new PublicChatTerminalRetentionOptionsValidator().Validate(
            "public chat retention",
            new PublicChatTerminalRetentionOptions { Duration = TimeSpan.Zero }
        );

        retry.Failed.ShouldBeTrue();
        retry.Failures.Count().ShouldBeGreaterThanOrEqualTo(3);
        retention.Failed.ShouldBeTrue();
    }

    [Test]
    public void PublicChatLifetime_Validating_RequiresPositiveAtMostSixtySeconds()
    {
        ValidateLifetime(null).Failed.ShouldBeTrue();
        ValidateLifetime(TimeSpan.Zero).Failed.ShouldBeTrue();
        ValidateLifetime(TimeSpan.FromTicks(-1)).Failed.ShouldBeTrue();
        ValidateLifetime(TimeSpan.FromSeconds(60).Add(TimeSpan.FromTicks(1)))
            .Failed.ShouldBeTrue();
        ValidateLifetime(TimeSpan.FromSeconds(60)).Failed.ShouldBeFalse();
    }

    [Test]
    [Arguments("IrcSession")]
    [Arguments("EventSubSession")]
    [Arguments("EventSubChannelRecovery")]
    [Arguments("PublicChatRetry")]
    [Arguments("PublicChatDeliveryLifetime")]
    [Arguments("PublicChatTerminalRetention")]
    public void RequiredPolicySectionMissing_Binding_FailsWithBoundaryName(string sectionName)
    {
        var values = ValidConfiguration()
            .Where(pair =>
                !pair.Key.StartsWith(
                    $"TwitchBot:Policies:{sectionName}:",
                    StringComparison.Ordinal
                )
            )
            .ToDictionary();
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection("TwitchBot");

        var exception = Should.Throw<OptionsValidationException>(() =>
            TwitchBotPolicies.BindRequired(section)
        );

        exception.OptionsName.ShouldContain(sectionName);
        exception.Message.ShouldNotContain("secret", Case.Insensitive);
        exception.Message.ShouldNotContain("message content", Case.Insensitive);
    }

    [Test]
    public void MaximumDelayBeforeDelay_Mapping_FailsExplicitCrossFieldRule()
    {
        var options = ValidOptions();
        options.EventSubChannelRecovery.MaximumDelay = options.EventSubChannelRecovery.Delay
            - TimeSpan.FromTicks(1);

        var exception = Should.Throw<OptionsValidationException>(() =>
            TwitchBotPolicies.FromOptions(options)
        );

        exception.OptionsName.ShouldContain(nameof(TwitchBotPolicyOptions.EventSubChannelRecovery));
        exception.Failures.ShouldContain(
            "MaximumDelay must be greater than or equal to Delay."
        );
    }

    [Test]
    public void CompleteConfiguration_Binding_MapsNamedImmutablePolicies()
    {
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(ValidConfiguration())
            .Build()
            .GetSection("TwitchBot");

        var policies = TwitchBotPolicies.BindRequired(section);

        policies.IrcSession.AttemptLimit.ShouldBe(5);
        policies.EventSubSession.AttemptTimeout.ShouldBe(TimeSpan.FromMinutes(2));
        policies.EventSubChannelRecovery.DelayBackoffType.ShouldBe(
            DelayBackoffType.Exponential
        );
        policies.PublicChatRetry.MaximumDelay.ShouldBe(TimeSpan.FromSeconds(30));
        policies.PublicChatDeliveryLifetime.MaximumAge.ShouldBe(TimeSpan.FromSeconds(30));
        policies.PublicChatTerminalRetention.Duration.ShouldBe(TimeSpan.FromDays(7));
    }

    [Test]
    public void MutablePolicyOptions_Mapping_IsolatesSnapshotFromLaterMutation()
    {
        var options = ValidOptions();
        var policies = TwitchBotPolicies.FromOptions(options);

        options.IrcSession.AttemptLimit = 99;
        options.IrcSession.Delay = TimeSpan.FromHours(1);
        options.PublicChatTerminalRetention.Duration = TimeSpan.FromDays(99);
        options.PublicChatDeliveryLifetime.MaximumAge = TimeSpan.FromSeconds(1);

        policies.IrcSession.AttemptLimit.ShouldBe(5);
        policies.IrcSession.Delay.ShouldBe(TimeSpan.FromSeconds(3));
        policies.PublicChatTerminalRetention.Duration.ShouldBe(TimeSpan.FromDays(7));
        policies.PublicChatDeliveryLifetime.MaximumAge.ShouldBe(TimeSpan.FromSeconds(30));
    }

    private static TwitchBotPolicyOptions ValidOptions()
    {
        return new()
        {
            IrcSession = new IrcSessionResilienceOptions
            {
                AttemptLimit = 5,
                Delay = TimeSpan.FromSeconds(3),
                MaximumDelay = TimeSpan.FromSeconds(30),
                DelayBackoffType = DelayBackoffType.Exponential,
                AttemptTimeout = TimeSpan.FromMinutes(2),
            },
            EventSubSession = new EventSubSessionResilienceOptions
            {
                AttemptLimit = 5,
                Delay = TimeSpan.FromSeconds(5),
                MaximumDelay = TimeSpan.FromMinutes(1),
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
    }

    private static Dictionary<string, string?> ValidConfiguration()
    {
        return new()
        {
            ["TwitchBot:Policies:IrcSession:AttemptLimit"] = "5",
            ["TwitchBot:Policies:IrcSession:Delay"] = "00:00:03",
            ["TwitchBot:Policies:IrcSession:MaximumDelay"] = "00:00:30",
            ["TwitchBot:Policies:IrcSession:DelayBackoffType"] = "Exponential",
            ["TwitchBot:Policies:IrcSession:AttemptTimeout"] = "00:02:00",
            ["TwitchBot:Policies:EventSubSession:AttemptLimit"] = "5",
            ["TwitchBot:Policies:EventSubSession:Delay"] = "00:00:05",
            ["TwitchBot:Policies:EventSubSession:MaximumDelay"] = "00:01:00",
            ["TwitchBot:Policies:EventSubSession:DelayBackoffType"] = "Exponential",
            ["TwitchBot:Policies:EventSubSession:AttemptTimeout"] = "00:02:00",
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
    }

    private static ValidateOptionsResult ValidateLifetime(TimeSpan? maximumAge)
    {
        return new PublicChatDeliveryLifetimeOptionsValidator().Validate(
            "public chat lifetime",
            new PublicChatDeliveryLifetimeOptions { MaximumAge = maximumAge }
        );
    }
}
