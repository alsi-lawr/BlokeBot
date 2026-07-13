using BlokeBot.Commands;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Commands.Tests;

public sealed class TwitchModeratorPolicyTests
{
    [Test]
    public void ChannelOwner_CheckingModeratorStatus_ReturnsTrue()
    {
        TwitchModeratorPolicy.IsModerator(Message("streamer", "streamer")).ShouldBeTrue();
    }

    [Test]
    public void ModTagPresent_CheckingModeratorStatus_ReturnsTrue()
    {
        TwitchModeratorPolicy
            .IsModerator(
                Message("viewer", "streamer", new Dictionary<string, string> { ["mod"] = "1" })
            )
            .ShouldBeTrue();
    }

    [Test]
    public void ModeratorOrBroadcasterBadge_CheckingModeratorStatus_ReturnsTrue()
    {
        TwitchModeratorPolicy
            .IsModerator(
                Message(
                    "viewer",
                    "streamer",
                    new Dictionary<string, string> { ["badges"] = "subscriber/12,moderator/1" }
                )
            )
            .ShouldBeTrue();
    }

    [Test]
    public void ViewerWithoutModeratorSignals_CheckingModeratorStatus_ReturnsFalse()
    {
        TwitchModeratorPolicy.IsModerator(Message("viewer", "streamer")).ShouldBeFalse();
    }

    private static ChatMessage Message(
        string login,
        string channel,
        IReadOnlyDictionary<string, string>? tags = null
    )
    {
        return new(
            login,
            channel,
            "!command",
            string.Empty,
            tags ?? new Dictionary<string, string>()
        );
    }
}
