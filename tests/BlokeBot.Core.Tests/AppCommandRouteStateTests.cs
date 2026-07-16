using BlokeBot.Core.Features.Commands;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class AppCommandRouteStateTests
{
    [Test]
    public void HostAndGuessingProfileRoutes_Matching_DispatchToTypedHandlers()
    {
        AppCommandRouteState host = new AppCommandRouteState.Host(7);
        AppCommandRouteState guessingProfile = new AppCommandRouteState.GuessingProfile(7, 11);

        host.Match(
                static route => $"host:{route.HostId}",
                static route => $"profile:{route.HostId}:{route.ProfileId}"
            )
            .ShouldBe("host:7");
        guessingProfile
            .Match(
                static route => $"host:{route.HostId}",
                static route => $"profile:{route.HostId}:{route.ProfileId}"
            )
            .ShouldBe("profile:7:11");
    }

    [Test]
    public void InvalidIdentifiers_ConstructingRoutes_Rejects()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AppCommandRouteState.Host(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new AppCommandRouteState.Host(-1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new AppCommandRouteState.GuessingProfile(0, 1)
        );
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new AppCommandRouteState.GuessingProfile(1, 0)
        );
    }
}
