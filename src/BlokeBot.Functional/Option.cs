namespace BlokeBot.Functional;

public sealed record Option<T>
{
    private readonly OptionState _state;

    private Option(OptionState state)
    {
        _state = state;
    }

    public static Option<T> None { get; } = new(new NoneState());

    public static Option<T> Some(T value)
    {
        return FromNullable(value);
    }

    public static Option<T> FromNullable(T? value)
    {
        return value is null ? None : new(new SomeState(value));
    }

    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)
    {
        return _state.Match(some, none);
    }

    public Option<TMapped> Map<TMapped>(Func<T, TMapped?> map)
    {
        return Match(value => Option<TMapped>.FromNullable(map(value)), () => Option<TMapped>.None);
    }

    public Option<TMapped> Bind<TMapped>(Func<T, Option<TMapped>> bind)
    {
        return Match(bind, () => Option<TMapped>.None);
    }

    private abstract record OptionState
    {
        public abstract TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none);
    }

    private sealed record SomeState(T Value) : OptionState
    {
        public override TResult Match<TResult>(Func<T, TResult> some, Func<TResult> _)
        {
            return some(Value);
        }
    }

    private sealed record NoneState : OptionState
    {
        public override TResult Match<TResult>(Func<T, TResult> _, Func<TResult> none)
        {
            return none();
        }
    }
}
