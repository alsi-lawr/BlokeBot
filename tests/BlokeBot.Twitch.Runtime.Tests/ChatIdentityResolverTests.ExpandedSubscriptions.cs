using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed partial class ChatIdentityResolverTests
{
    [Test]
    public async Task ModerateSubscription_ActuallySendsVersionTwo_AndBroadcasterModeratorCondition()
    {
        using var factory = new IdentityHttpClientFactory(
            """{"data":[{"id":"channel-id","login":"channel"}]}"""
        );
        var operations = CreateEventSubOperations(
            factory,
            new ScriptedBroadcasterAccountProvider(
                Result<BotAccount, AccessTokenUnavailableReason>.Success(
                    new("channel", "broadcaster-secret")
                )
            )
        );
        var account = await ResolveBroadcasterAsync(operations);
        var outcome = await operations.CreateExactSubscriptionAsync(
            "channel",
            account,
            new("channel.moderate", "2"),
            CancellationToken.None
        );
        _ = outcome.ShouldBeOfType<EventSubSubscriptionSetupOutcome.Created>();
        var request = factory.EventSubRequests.Single();
        request.Type.ShouldBe("channel.moderate");
        request.Version.ShouldBe("2");
        request
            .Condition.ShouldNotBeNull()
            .ShouldBe(
                new Dictionary<string, string>
                {
                    { "broadcaster_user_id", "channel-id" },
                    { "moderator_user_id", "channel-id" },
                }
            );
        request.Authorization.ShouldBe("Bearer app-access-token");
    }
}
