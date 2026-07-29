using System.Reflection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubAuthorizationContextCompatibilityTests
{
    [Test]
    public void PublicAuthorizationContext_PreservesThreeHandlerMatchWithoutRaidVariant()
    {
        EventSubAuthorizationContext
            .ConfiguredBotOperationsAuthority.Match(
                _ => "configured-bot",
                _ => "configured-bot-operations",
                _ => "broadcaster"
            )
            .ShouldBe("configured-bot-operations");

        typeof(EventSubAuthorizationContext)
            .GetNestedTypes(BindingFlags.Public)
            .Select(type => type.Name)
            .Order()
            .ShouldBe([
                nameof(EventSubAuthorizationContext.Broadcaster),
                nameof(EventSubAuthorizationContext.ConfiguredBot),
                nameof(EventSubAuthorizationContext.ConfiguredBotOperations),
            ]);
        typeof(EventSubAuthorizationContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(property => property.Name)
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
