using Shouldly;

namespace BlokeBot.Functional.Tests;

public sealed class ValidationTests
{
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

    private static IReadOnlyList<TError> GetErrors<TValue, TError>(
        Validation<TValue, TError> validation
    ) => validation.Match<IReadOnlyList<TError>>(static _ => [], static errors => errors);
}
