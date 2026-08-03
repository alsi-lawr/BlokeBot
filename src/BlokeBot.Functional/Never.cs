using System.Diagnostics;

namespace BlokeBot.Functional;

public abstract record Never
{
    private Never() { }
}

public static class NeverIO
{
    public static async ValueTask<TValue> RunAsync<TValue>(
        this IO<TValue, Never> operation,
        CancellationToken cancellationToken
    )
    {
        var result = await operation.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return result.Match(static value => value, static _ => throw new UnreachableException());
    }
}
