using Shouldly;

namespace BlokeBot.Commands.Tests;

public sealed class CommandAliasPolicyTests
{
    private enum TestKind
    {
        One,
        Two,
        Three,
    }

    [Test]
    public void NoisyDuplicateAliases_Normalizing_TrimsPrefixesAndDeduplicates()
    {
        CommandAliasNormalizer.Normalize(" !POINTS ").ShouldBe("points");
        CommandAliasNormalizer.Split("!One, TWO, one,  ").ShouldBe(["one", "two"]);
        CommandAliasNormalizer.SplitPreservingOrder("!Two, ONE, two").ShouldBe(["two", "one"]);
    }

    [Test]
    public void DuplicateDraftAliases_Validating_ReturnsNormalizedDuplicate()
    {
        var duplicate = CommandAliasPolicy.FindDuplicateAlias([
            new CommandAliasDraft<TestKind>(TestKind.One, "points"),
            new CommandAliasDraft<TestKind>(TestKind.Two, "!POINTS"),
        ]);

        duplicate.ShouldBe("points");
    }

    [Test]
    public void AliasOwnedByOtherKind_CheckingCollision_ReturnsCollision()
    {
        var collision = CommandAliasPolicy.FindCollision(
            [new CommandAliasDraft<TestKind>(TestKind.One, "points")],
            new HashSet<TestKind> { TestKind.One },
            [
                new CommandAliasOwnership<TestKind>(TestKind.Two, "points"),
                new CommandAliasOwnership<TestKind>(TestKind.One, "owned"),
            ]
        );

        collision.ShouldBe("points");
    }

    [Test]
    public void AliasOwnedByReplacedKind_CheckingCollision_ReturnsNoCollision()
    {
        var collision = CommandAliasPolicy.FindCollision(
            [new CommandAliasDraft<TestKind>(TestKind.One, "points")],
            new HashSet<TestKind> { TestKind.One, TestKind.Two },
            [new CommandAliasOwnership<TestKind>(TestKind.Two, "points")]
        );

        collision.ShouldBeNull();
    }
}
