using Shouldly;
using TUnit.Core;

namespace BlokeBot.Functional.Tests;

public sealed class OptionTests
{
    [Test]
    public void NonNullValue_CreatingSome_MatchesValueBranchOnly()
    {
        var noneInvoked = false;
        var option = Option<string>.Some("value");

        var matched = option.Match(
            value => value,
            () =>
            {
                noneInvoked = true;
                return "unexpected";
            }
        );

        matched.ShouldBe("value");
        noneInvoked.ShouldBeFalse();
    }

    [Test]
    public void NullValue_CreatingOption_ReturnsNone()
    {
        Option<string>.FromNullable(null).ShouldBe(Option<string>.None);
        Option<string?>.Some(null).ShouldBe(Option<string?>.None);
    }

    [Test]
    public void None_Matching_UsesNoneBranchOnly()
    {
        var someInvoked = false;

        var matched = Option<int>.None.Match(
            _ =>
            {
                someInvoked = true;
                return "unexpected";
            },
            () => "none"
        );

        matched.ShouldBe("none");
        someInvoked.ShouldBeFalse();
    }

    [Test]
    public void Some_Mapping_TransformsValue()
    {
        var mapped = Option<int>.Some(21).Map(value => value * 2);

        mapped.Match(value => value, () => 0).ShouldBe(42);
    }

    [Test]
    public void Some_MappingToNull_ReturnsNone()
    {
        var mapped = Option<string>.Some("value").Map<string>(_ => null);

        mapped.ShouldBe(Option<string>.None);
    }

    [Test]
    public void None_Mapping_PreservesNoneWithoutInvokingMap()
    {
        var mapInvoked = false;

        var mapped = Option<int>.None.Map(_ =>
        {
            mapInvoked = true;
            return 42;
        });

        mapped.ShouldBe(Option<int>.None);
        mapInvoked.ShouldBeFalse();
    }

    [Test]
    public void Some_Binding_ComposesOption()
    {
        var bound = Option<int>.Some(21).Bind(value => Option<string>.Some((value * 2).ToString()));

        bound.Match(value => value, () => string.Empty).ShouldBe("42");
    }

    [Test]
    public void Some_BindingToNone_ReturnsNone()
    {
        var bound = Option<int>.Some(21).Bind(_ => Option<string>.None);

        bound.ShouldBe(Option<string>.None);
    }

    [Test]
    public void None_Binding_PreservesNoneWithoutInvokingBind()
    {
        var bindInvoked = false;

        var bound = Option<int>.None.Bind(_ =>
        {
            bindInvoked = true;
            return Option<string>.Some("unexpected");
        });

        bound.ShouldBe(Option<string>.None);
        bindInvoked.ShouldBeFalse();
    }
}
