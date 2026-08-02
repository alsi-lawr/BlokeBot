using BlokeBot.Core.Features.Commands;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AppCommandRouteStateTests
{
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
