using System.Globalization;
using Shouldly;

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
        var mapped = Option<int>.Some(21).Map(static value => value * 2);

        mapped.Match(static value => value, static () => 0).ShouldBe(42);
    }

    [Test]
    public void Some_MappingToNull_ReturnsNone()
    {
        var mapped = Option<string>.Some("value").Map<string>(static _ => null);

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
        var bound = Option<int>
            .Some(21)
            .Bind(static value =>
                Option<string>.Some((value * 2).ToString(CultureInfo.InvariantCulture))
            );

        bound.Match(static value => value, static () => string.Empty).ShouldBe("42");
    }

    [Test]
    public void Some_BindingToNone_ReturnsNone()
    {
        var bound = Option<int>.Some(21).Bind(static _ => Option<string>.None);

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
