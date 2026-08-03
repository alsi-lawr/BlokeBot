using System.Globalization;
using Shouldly;

namespace BlokeBot.Functional.Tests;

public sealed class ResultTests
{
    [Test]
    public void Success_CreatingAndMatching_UsesSuccessBranchOnly()
    {
        var errorInvoked = false;
        var result = Result<int, TestError>.Success(21);

        var matched = result.Match(
            value => value * 2,
            _ =>
            {
                errorInvoked = true;
                return 0;
            }
        );

        matched.ShouldBe(42);
        errorInvoked.ShouldBeFalse();
    }

    [Test]
    public void Error_CreatingAndMatching_UsesErrorBranchOnly()
    {
        var successInvoked = false;
        var expected = new TestError("invalid");
        var result = Result<int, TestError>.Error(expected);

        var matched = result.Match(
            _ =>
            {
                successInvoked = true;
                return new TestError("unexpected");
            },
            error => error
        );

        matched.ShouldBe(expected);
        successInvoked.ShouldBeFalse();
    }

    [Test]
    public void Success_Mapping_TransformsValue()
    {
        var mapped = Result<int, TestError>.Success(21).Map(static value => value * 2);

        mapped.Match(static value => value, static _ => 0).ShouldBe(42);
    }

    [Test]
    public void Error_Mapping_PreservesErrorWithoutInvokingMap()
    {
        var mapInvoked = false;
        var expected = new TestError("invalid");

        var mapped = Result<int, TestError>
            .Error(expected)
            .Map(_ =>
            {
                mapInvoked = true;
                return 42;
            });

        mapped.Match(_ => new TestError("unexpected"), error => error).ShouldBe(expected);
        mapInvoked.ShouldBeFalse();
    }

    [Test]
    public void Success_Binding_ComposesResult()
    {
        var bound = Result<int, TestError>
            .Success(21)
            .Bind(static value =>
                Result<string, TestError>.Success(
                    (value * 2).ToString(CultureInfo.InvariantCulture)
                )
            );

        bound.Match(static value => value, static _ => string.Empty).ShouldBe("42");
    }

    [Test]
    public void Success_BindingToError_ReturnsBoundError()
    {
        var expected = new TestError("invalid");

        var bound = Result<int, TestError>
            .Success(21)
            .Bind(_ => Result<string, TestError>.Error(expected));

        bound.Match(_ => new TestError("unexpected"), error => error).ShouldBe(expected);
    }

    [Test]
    public void Error_Binding_PreservesErrorWithoutInvokingBind()
    {
        var bindInvoked = false;
        var expected = new TestError("invalid");

        var bound = Result<int, TestError>
            .Error(expected)
            .Bind(_ =>
            {
                bindInvoked = true;
                return Result<string, TestError>.Success("unexpected");
            });

        bound.Match(_ => new TestError("unexpected"), error => error).ShouldBe(expected);
        bindInvoked.ShouldBeFalse();
    }

    private sealed record TestError(string Code);
}
