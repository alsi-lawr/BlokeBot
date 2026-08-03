using Shouldly;

namespace BlokeBot.Functional.Tests;

public sealed class ValidationTests
{
    [Test]
    public void Valid_Mapping_TransformsValue()
    {
        var mapped = Validation<int, string>.Valid(21).Map(static value => value * 2);

        mapped.Match(static value => value, static _ => 0).ShouldBe(42);
    }

    [Test]
    public void Invalid_Mapping_PreservesErrorsWithoutInvokingMap()
    {
        var mapInvoked = false;
        var validation = Validation<int, string>.Invalid("invalid");

        var mapped = validation.Map(_ =>
        {
            mapInvoked = true;
            return 42;
        });

        GetErrors(mapped).ShouldBe(["invalid"]);
        mapInvoked.ShouldBeFalse();
    }

    [Test]
    public void ValidValues_Combining_ProducesValidValue()
    {
        var combineInvocations = 0;

        var combined = Validation<int, string>
            .Valid(20)
            .Combine(
                Validation<int, string>.Valid(22),
                (first, second) =>
                {
                    combineInvocations++;
                    return first + second;
                }
            );

        combined.Match(value => value, _ => 0).ShouldBe(42);
        combineInvocations.ShouldBe(1);
    }

    [Test]
    public void TwoInvalidValues_Combining_AccumulatesBothErrors()
    {
        var combined = Validation<int, string>
            .Invalid("first")
            .Combine(
                Validation<int, string>.Invalid("second"),
                static (first, second) => first + second
            );

        GetErrors(combined).ShouldBe(["first", "second"]);
    }

    [Test]
    public void MultipleInvalidValues_Combining_PreservesStableInputOrder()
    {
        var combined = Validation<int, string>
            .Invalid("first", "second")
            .Combine(
                Validation<int, string>.Invalid("third"),
                static (first, second) => first + second
            )
            .Combine(
                Validation<int, string>.Invalid("fourth", "fifth"),
                static (first, second) => first + second
            );

        GetErrors(combined).ShouldBe(["first", "second", "third", "fourth", "fifth"]);
    }

    [Test]
    public void MixedValidAndInvalidValues_Combining_PreservesInvalidErrors()
    {
        var validThenInvalid = Validation<int, string>
            .Valid(1)
            .Combine(
                Validation<int, string>.Invalid("right"),
                static (first, second) => first + second
            );
        var invalidThenValid = Validation<int, string>
            .Invalid("left")
            .Combine(Validation<int, string>.Valid(2), static (first, second) => first + second);

        GetErrors(validThenInvalid).ShouldBe(["right"]);
        GetErrors(invalidThenValid).ShouldBe(["left"]);
    }

    [Test]
    public void Invalid_Creation_RequiresAnErrorAndCopiesAdditionalErrors()
    {
        var additionalErrors = new[] { "second", "third" };
        var validation = Validation<int, string>.Invalid("first", additionalErrors);

        additionalErrors[0] = "changed";
        var errors = GetErrors(validation);

        errors.ShouldBe(["first", "second", "third"]);
        errors.Count.ShouldBe(3);
        (errors is IList<string>).ShouldBeFalse();
        GetErrors(Validation<int, string>.Invalid("only")).Count.ShouldBe(1);
    }

    [Test]
    public void Invalid_ConvertingToResult_UsesSelectedAggregateError()
    {
        var result = Validation<int, string>
            .Invalid("first", "second")
            .ToResult(static errors => new AggregateError(string.Join("|", errors)));

        result
            .Match(static _ => new AggregateError("unexpected"), static error => error)
            .ShouldBe(new AggregateError("first|second"));
    }

    [Test]
    public void Valid_ConvertingToResult_DoesNotCreateAggregateError()
    {
        var aggregateInvoked = false;

        var result = Validation<int, string>
            .Valid(42)
            .ToResult(_ =>
            {
                aggregateInvoked = true;
                return new AggregateError("unexpected");
            });

        result.Match(value => value, _ => 0).ShouldBe(42);
        aggregateInvoked.ShouldBeFalse();
    }

    [Test]
    public void MapFunction_Throwing_PropagatesOriginalException()
    {
        var expected = new TestException();

        var thrown = Should.Throw<TestException>(() =>
            Validation<int, string>.Valid(42).Map<int>(_ => throw expected)
        );

        thrown.ShouldBe(expected);
    }

    [Test]
    public void CombineFunction_Throwing_PropagatesOriginalException()
    {
        var expected = new TestException();

        var thrown = Should.Throw<TestException>(() =>
            Validation<int, string>
                .Valid(20)
                .Combine<int, int>(Validation<int, string>.Valid(22), (_, _) => throw expected)
        );

        thrown.ShouldBe(expected);
    }

    private static IReadOnlyList<TError> GetErrors<TValue, TError>(
        Validation<TValue, TError> validation
    ) => validation.Match<IReadOnlyList<TError>>(static _ => [], static errors => errors);

    private sealed record AggregateError(string Message);

    private sealed class TestException : Exception { }
}
