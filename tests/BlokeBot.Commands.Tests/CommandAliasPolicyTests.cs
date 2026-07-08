using BlokeBot.Commands;
using Shouldly;
using TUnit.Core;

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
    public void Normalizer_trims_bang_lowercases_splits_and_deduplicates()
    {
        CommandAliasNormalizer.Normalize(" !POINTS ").ShouldBe("points");
        CommandAliasNormalizer.Split("!One, TWO, one,  ").ShouldBe(["one", "two"]);
    }

    [Test]
    public void Alias_policy_detects_duplicates_inside_drafts()
    {
        var duplicate = CommandAliasPolicy.FindDuplicateAlias([
            new CommandAliasDraft<TestKind>(TestKind.One, "points"),
            new CommandAliasDraft<TestKind>(TestKind.Two, "!POINTS"),
        ]);

        duplicate.ShouldBe("points");
    }

    [Test]
    public void Alias_policy_detects_collisions_outside_owned_kinds()
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
    public void Alias_policy_ignores_collisions_inside_owned_kinds()
    {
        var collision = CommandAliasPolicy.FindCollision(
            [new CommandAliasDraft<TestKind>(TestKind.One, "points")],
            new HashSet<TestKind> { TestKind.One, TestKind.Two },
            [new CommandAliasOwnership<TestKind>(TestKind.Two, "points")]
        );

        collision.ShouldBeNull();
    }
}
