using System.Collections;

namespace BlokeBot.Functional;

public sealed record Validation<T, TError>
{
    private readonly ValidationState _state;

    private Validation(ValidationState state)
    {
        _state = state;
    }

    public static Validation<T, TError> Valid(T value) => new(new ValidState(value));

    public static Validation<T, TError> Invalid(
        TError firstError,
        params TError[] additionalErrors
    ) =>
        new(
            new InvalidState(
                NonEmptyValidationErrors<TError>.Create(firstError, additionalErrors)
            )
        );

    public TResult Match<TResult>(
        Func<T, TResult> valid,
        Func<IReadOnlyList<TError>, TResult> invalid
    ) => MatchState(valid, errors => invalid(errors));

    public Validation<TMapped, TError> Map<TMapped>(Func<T, TMapped> map) =>
        MatchState(
            value => Validation<TMapped, TError>.Valid(map(value)),
            Validation<TMapped, TError>.FromInvalid
        );

    public Validation<TResult, TError> Combine<TOther, TResult>(
        Validation<TOther, TError> other,
        Func<T, TOther, TResult> combine
    ) =>
        MatchState(
            first =>
                other.MatchState(
                    second => Validation<TResult, TError>.Valid(combine(first, second)),
                    Validation<TResult, TError>.FromInvalid
                ),
            firstErrors =>
                other.MatchState(
                    _ => Validation<TResult, TError>.FromInvalid(firstErrors),
                    secondErrors =>
                        Validation<TResult, TError>.FromInvalid(
                            firstErrors.Append(secondErrors)
                        )
                )
        );

    public Result<T, TAggregateError> ToResult<TAggregateError>(
        Func<IReadOnlyList<TError>, TAggregateError> aggregateErrors
    ) =>
        MatchState(
            Result<T, TAggregateError>.Success,
            errors => Result<T, TAggregateError>.Error(aggregateErrors(errors))
        );

    private static Validation<T, TError> FromInvalid(
        NonEmptyValidationErrors<TError> errors
    ) => new(new InvalidState(errors));

    private TResult MatchState<TResult>(
        Func<T, TResult> valid,
        Func<NonEmptyValidationErrors<TError>, TResult> invalid
    ) => _state.Match(valid, invalid);

    private abstract record ValidationState
    {
        public abstract TResult Match<TResult>(
            Func<T, TResult> valid,
            Func<NonEmptyValidationErrors<TError>, TResult> invalid
        );
    }

    private sealed record ValidState(T Value) : ValidationState
    {
        public override TResult Match<TResult>(
            Func<T, TResult> valid,
            Func<NonEmptyValidationErrors<TError>, TResult> _
        ) => valid(Value);
    }

    private sealed record InvalidState(NonEmptyValidationErrors<TError> Errors) : ValidationState
    {
        public override TResult Match<TResult>(
            Func<T, TResult> _,
            Func<NonEmptyValidationErrors<TError>, TResult> invalid
        ) => invalid(Errors);
    }
}

internal sealed class NonEmptyValidationErrors<TError>
    : IReadOnlyList<TError>,
        IEquatable<NonEmptyValidationErrors<TError>>
{
    private readonly TError[] _errors;

    private NonEmptyValidationErrors(TError[] errors)
    {
        _errors = errors;
    }

    public int Count => _errors.Length;

    public TError this[int index] => _errors[index];

    internal static NonEmptyValidationErrors<TError> Create(
        TError firstError,
        ReadOnlySpan<TError> additionalErrors
    )
    {
        var errors = new TError[additionalErrors.Length + 1];
        errors[0] = firstError;
        additionalErrors.CopyTo(errors.AsSpan(1));
        return new(errors);
    }

    internal NonEmptyValidationErrors<TError> Append(
        NonEmptyValidationErrors<TError> other
    )
    {
        var errors = new TError[Count + other.Count];
        _errors.CopyTo(errors, 0);
        other._errors.CopyTo(errors, Count);
        return new(errors);
    }

    public IEnumerator<TError> GetEnumerator() =>
        ((IEnumerable<TError>)_errors).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(NonEmptyValidationErrors<TError>? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Count != other.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<TError>.Default;
        for (var index = 0; index < Count; index++)
        {
            if (!comparer.Equals(_errors[index], other._errors[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is NonEmptyValidationErrors<TError> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var error in _errors)
        {
            hash.Add(error);
        }

        return hash.ToHashCode();
    }
}
