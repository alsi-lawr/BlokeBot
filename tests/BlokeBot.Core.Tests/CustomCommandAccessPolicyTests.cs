using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandAccessPolicyTests
{
    [Test]
    public void GrantMatrix_Evaluating_AdmitsOnlyComposedAccess()
    {
        var publicCommand = Command(allowEveryone: true);
        var streamerOnly = Command();
        var moderators = Command(allowModerators: true);
        var selected = Command(
            allowedUsers:
            [
                new CustomCommandAllowedUser
                {
                    TwitchUserId = "selected-id",
                    Login = "old_login",
                    DisplayName = "Old name",
                },
            ]
        );
        var moderator = Message("moderator", "moderator-id", moderator: true);
        var selectedAfterRename = Message("renamed", "selected-id");

        CustomCommandAccessPolicy
            .Allows("streamer", publicCommand, Message("viewer", null))
            .ShouldBeTrue();
        CustomCommandAccessPolicy
            .Allows("streamer", streamerOnly, Message("streamer", null))
            .ShouldBeTrue();
        CustomCommandAccessPolicy.Allows("streamer", streamerOnly, moderator).ShouldBeFalse();
        CustomCommandAccessPolicy.Allows("streamer", moderators, moderator).ShouldBeTrue();
        CustomCommandAccessPolicy.Allows("streamer", selected, moderator).ShouldBeFalse();
        CustomCommandAccessPolicy.Allows("streamer", selected, selectedAfterRename).ShouldBeTrue();
        CustomCommandAccessPolicy
            .Allows("streamer", selected, Message("old_login", null))
            .ShouldBeFalse();
        CustomCommandAccessPolicy
            .Allows("streamer", selected, Message("old_login", "different-id"))
            .ShouldBeFalse();
    }

    private static CustomCommand Command(
        bool allowEveryone = false,
        bool allowModerators = false,
        List<CustomCommandAllowedUser>? allowedUsers = null
    ) =>
        new()
        {
            AllowEveryone = allowEveryone,
            AllowModerators = allowModerators,
            AllowedUsers = allowedUsers ?? [],
        };

    private static ChatMessage Message(string login, string? twitchUserId, bool moderator = false)
    {
        var tags = new Dictionary<string, string>();
        if (twitchUserId is not null)
        {
            tags["user-id"] = twitchUserId;
        }
        if (moderator)
        {
            tags["mod"] = "1";
        }
        return new(login, "streamer", "!command", "raw", tags);
    }
}
