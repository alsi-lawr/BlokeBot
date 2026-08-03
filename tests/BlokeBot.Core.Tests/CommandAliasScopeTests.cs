using BlokeBot.Core.Features.Commands;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CommandAliasScopeTests
{
    [Test]
    public void InvalidProfileIds_ConstructingProfileScope_Rejects()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => new CommandAliasScope.Profile(0));
        _ = Should.Throw<ArgumentOutOfRangeException>(() => new CommandAliasScope.Profile(-1));
    }
}
