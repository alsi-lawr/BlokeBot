using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Status;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class FollowerOnlyChatReadinessPresentationTests
{
    [Test]
    public void ReadinessStates_ProjectToDistinctSetupActions()
    {
        var eligibleAt = new DateTimeOffset(2026, 7, 18, 12, 30, 0, TimeSpan.Zero);

        FollowerOnlyChatReadinessPresentation
            .From(new FollowerOnlyChatReadiness.NotRequired())
            .State.ShouldBe(FollowerOnlyChatSetupState.NotRequired);
        FollowerOnlyChatReadinessPresentation
            .From(new FollowerOnlyChatReadiness.Exempt(FollowerOnlyChatExemption.Moderator))
            .State.ShouldBe(FollowerOnlyChatSetupState.Exempt);
        FollowerOnlyChatReadinessPresentation
            .From(new FollowerOnlyChatReadiness.EligibleNow())
            .State.ShouldBe(FollowerOnlyChatSetupState.Eligible);
        var waiting = FollowerOnlyChatReadinessPresentation.From(
            new FollowerOnlyChatReadiness.WaitingUntil(eligibleAt)
        );
        waiting.State.ShouldBe(FollowerOnlyChatSetupState.Waiting);
        waiting.EligibleAtUtc.ShouldBe(eligibleAt);
        FollowerOnlyChatReadinessPresentation
            .From(new FollowerOnlyChatReadiness.NotFollowing())
            .State.ShouldBe(FollowerOnlyChatSetupState.NotFollowing);
        FollowerOnlyChatReadinessPresentation
            .From(
                new FollowerOnlyChatReadiness.UnableToVerify(
                    FollowerOnlyChatVerificationFailure.MissingFollowReadScope
                )
            )
            .State.ShouldBe(FollowerOnlyChatSetupState.ReconnectRequired);
        FollowerOnlyChatReadinessPresentation
            .From(
                new FollowerOnlyChatReadiness.UnableToVerify(
                    FollowerOnlyChatVerificationFailure.FollowReadUnavailable
                )
            )
            .State.ShouldBe(FollowerOnlyChatSetupState.UnableToVerify);
    }
}
