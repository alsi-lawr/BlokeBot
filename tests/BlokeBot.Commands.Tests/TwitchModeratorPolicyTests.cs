using BlokeBot.Commands;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Commands.Tests;

public sealed class TwitchModeratorPolicyTests
{
    [Test]
    public void Channel_owner_is_moderator()
    {
        TwitchModeratorPolicy.IsModerator(Message("streamer", "streamer")).ShouldBeTrue();
    }

    [Test]
    public void Mod_tag_marks_moderator()
    {
        TwitchModeratorPolicy
            .IsModerator(
                Message("viewer", "streamer", new Dictionary<string, string> { ["mod"] = "1" })
            )
            .ShouldBeTrue();
    }

    [Test]
    public void Badges_mark_broadcaster_or_moderator()
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
    public void Viewer_without_mod_signals_is_not_moderator()
    {
        TwitchModeratorPolicy.IsModerator(Message("viewer", "streamer")).ShouldBeFalse();
    }

    private static TwitchChatMessage Message(
        string login,
        string channel,
        IReadOnlyDictionary<string, string>? tags = null
    ) => new(login, channel, "!command", string.Empty, tags ?? new Dictionary<string, string>());
}
