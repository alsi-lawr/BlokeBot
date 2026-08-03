using System.Reflection;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubAuthorizationContextCompatibilityTests
{
    [Test]
    public void PublicAuthorizationContext_PreservesThreeHandlerMatchWithoutRaidVariant()
    {
        EventSubAuthorizationContext
            .ConfiguredBotOperationsAuthority.Match(
                static _ => "configured-bot",
                static _ => "configured-bot-operations",
                static _ => "broadcaster"
            )
            .ShouldBe("configured-bot-operations");

        typeof(EventSubAuthorizationContext)
            .GetNestedTypes(BindingFlags.Public)
            .Select(static type => type.Name)
            .Order()
            .ShouldBe([
                nameof(EventSubAuthorizationContext.Broadcaster),
                nameof(EventSubAuthorizationContext.ConfiguredBot),
                nameof(EventSubAuthorizationContext.ConfiguredBotOperations),
            ]);
        typeof(EventSubAuthorizationContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(static property => property.Name)
            .Order()
            .ShouldBe([
                nameof(EventSubAuthorizationContext.BroadcasterAuthority),
                nameof(EventSubAuthorizationContext.ConfiguredBotAuthority),
                nameof(EventSubAuthorizationContext.ConfiguredBotOperationsAuthority),
            ]);
        typeof(EventSubAuthorizationContext)
            .GetMethod(nameof(EventSubAuthorizationContext.Match))!
            .GetParameters()
            .Length.ShouldBe(3);
    }
}
