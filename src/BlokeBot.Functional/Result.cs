namespace BlokeBot.Functional;

public sealed record Result<TValue, TError>
{
    private readonly ResultState _state;

    private Result(ResultState state)
    {
        _state = state;
    }

    public static Result<TValue, TError> Success(TValue value)
    {
        return new(new SuccessState(value));
    }

    public static Result<TValue, TError> Error(TError error)
    {
        return new(new ErrorState(error));
    }

    public TResult Match<TResult>(Func<TValue, TResult> success, Func<TError, TResult> error)
    {
        return _state.Match(success, error);
    }

    public Result<TMapped, TError> Map<TMapped>(Func<TValue, TMapped> map)
    {
        return Match(
            value => Result<TMapped, TError>.Success(map(value)),
            Result<TMapped, TError>.Error
        );
    }

    public Result<TMapped, TError> Bind<TMapped>(Func<TValue, Result<TMapped, TError>> bind)
    {
        return Match(bind, Result<TMapped, TError>.Error);
    }

    private abstract record ResultState
    {
        public abstract TResult Match<TResult>(
            Func<TValue, TResult> success,
            Func<TError, TResult> error
        );
    }

    private sealed record SuccessState(TValue Value) : ResultState
    {
        public override TResult Match<TResult>(
            Func<TValue, TResult> success,
            Func<TError, TResult> _
        )
        {
            return success(Value);
        }
    }

    private sealed record ErrorState(TError Value) : ResultState
    {
        public override TResult Match<TResult>(Func<TValue, TResult> _, Func<TError, TResult> error)
        {
            return error(Value);
        }
    }
}
