using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.Points.Configuration;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BackgroundLoadIdentityTests
{
    [Test]
    public void HostStatusIdentity_EquivalentParameters_HaveEqualIdentity()
    {
        var first = HostBotChannelStatusPanelLoadIdentity.From(" Streamer ", null);
        var second = HostBotChannelStatusPanelLoadIdentity.From("streamer", string.Empty);

        first.ShouldNotBeNull();
        first.ShouldBe(second);
    }

    [Test]
    public void HostStatusIdentity_ChangedReloadKey_HasDifferentIdentity()
    {
        var first = HostBotChannelStatusPanelLoadIdentity.From("streamer", "first");
        var second = HostBotChannelStatusPanelLoadIdentity.From("STREAMER", "second");

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        first.ShouldNotBe(second);
    }

    [Test]
    public void PointsEligibilityIdentity_EquivalentHostLogins_HaveEqualIdentity()
    {
        var first = PointsEligibilityLoadIdentity.From(" Streamer ");
        var second = PointsEligibilityLoadIdentity.From("streamer");

        first.ShouldNotBeNull();
        first.ShouldBe(second);
    }

    [Test]
    public void BlankHostLogin_HasNoLoadIdentity()
    {
        HostBotChannelStatusPanelLoadIdentity.From(" ", "reload").ShouldBeNull();
        PointsEligibilityLoadIdentity.From(string.Empty).ShouldBeNull();
    }
}
