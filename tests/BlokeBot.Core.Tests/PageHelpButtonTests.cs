using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PageHelpButtonTests
{
    [Test]
    public void EveryConcreteHostSelectedFeatureRoute_HasUsefulRouteSpecificHelp()
    {
        const string RedirectOnlyRoute = "/twitch-operations";
        var routes = typeof(PageHelpButton)
            .Assembly.GetTypes()
            .Where(static type =>
                type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                    .Cast<AuthorizeAttribute>()
                    .Any(static attribute => attribute.Policy == "HostSelected")
            )
            .SelectMany(static type =>
                type.GetCustomAttributes(typeof(RouteAttribute), true)
                    .Cast<RouteAttribute>()
                    .Select(static route => route.Template)
            )
            .Where(static route => route != RedirectOnlyRoute)
            .Order(StringComparer.Ordinal)
            .ToArray();

        routes.ShouldBe([
            "/bounties",
            "/custom-commands/settings",
            "/guessing",
            "/guessing/settings",
            "/moments",
            "/overlays",
            "/points",
            "/points/settings",
            "/queues",
            "/requests",
            "/twitch-operations/channel-points",
            "/twitch-operations/clips-markers",
            "/twitch-operations/polls",
            "/twitch-operations/predictions",
            "/twitch-operations/shoutouts",
        ]);
        routes.ShouldAllBe(static route => PageHelpButton.HasUsefulHelpForPath(route));
    }

    [Test]
    public void WithdrawnAutomationEventsSurface_HasNoRouteOrPageHelp()
    {
        typeof(AutomationEventsPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .ShouldBeEmpty();
        PageHelpButton.HasUsefulHelpForPath("/automations/events").ShouldBeFalse();
    }
}
