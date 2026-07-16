using BlokeBot.Core.Features.Commands;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class CommandAliasScopeTests
{
    [Test]
    public void GlobalAndProfileScopes_Matching_DispatchToTheirTypedHandlers()
    {
        CommandAliasScope global = new CommandAliasScope.Global();
        CommandAliasScope profile = new CommandAliasScope.Profile(42);

        global
            .Match(static _ => "global", static value => $"profile:{value.ProfileId}")
            .ShouldBe("global");
        profile
            .Match(static _ => "global", static value => $"profile:{value.ProfileId}")
            .ShouldBe("profile:42");
    }

    [Test]
    public void InvalidProfileIds_ConstructingProfileScope_Rejects()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandAliasScope.Profile(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandAliasScope.Profile(-1));
    }
}
