namespace BlokeBot.Functional;

public sealed class IO<TValue, TError>
{
    private readonly Func<CancellationToken, ValueTask<Result<TValue, TError>>> _operation;

    private IO(Func<CancellationToken, ValueTask<Result<TValue, TError>>> operation) =>
        _operation = operation;

    public static IO<TValue, TError> Create(
        Func<CancellationToken, ValueTask<Result<TValue, TError>>> operation
    ) => new(operation);

    public static IO<TValue, TError> FromException<TException>(
        Func<CancellationToken, ValueTask<TValue>> operation,
        Func<TException, TError> mapException
    )
        where TException : Exception =>
        new(cancellationToken =>
            ExecuteMappedAsync<TException>(operation, mapException, cancellationToken)
        );

    public async ValueTask<Result<TValue, TError>> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _operation(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public IO<TMapped, TError> Map<TMapped>(Func<TValue, TMapped> map) =>
        IO<TMapped, TError>.Create(async cancellationToken =>
        {
            var result = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return result.Map(map);
        });

    public IO<TMapped, TError> Bind<TMapped>(Func<TValue, IO<TMapped, TError>> bind) =>
        IO<TMapped, TError>.Create(async cancellationToken =>
        {
            var result = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return await result
                .Match(
                    value => bind(value).ExecuteAsync(cancellationToken),
                    error => ValueTask.FromResult(Result<TMapped, TError>.Error(error))
                )
                .ConfigureAwait(false);
        });

    private static async ValueTask<Result<TValue, TError>> ExecuteMappedAsync<TException>(
        Func<CancellationToken, ValueTask<TValue>> operation,
        Func<TException, TError> mapException,
        CancellationToken cancellationToken
    )
        where TException : Exception
    {
        try
        {
            var value = await operation(cancellationToken).ConfigureAwait(false);
            return Result<TValue, TError>.Success(value);
        }
        catch (TException exception) when (exception is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Result<TValue, TError>.Error(mapException(exception));
        }
    }
}
