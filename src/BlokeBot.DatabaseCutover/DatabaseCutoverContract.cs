namespace BlokeBot.DatabaseCutover;

public sealed record DatabaseCutoverOptions(
    string StateDirectory,
    string SqliteDatabasePath,
    string PostgreSqlAdministratorConnectionStringFile,
    string PostgreSqlApplicationConnectionStringFile,
    Guid? OperationId,
    int BatchSize = 500
);

public abstract record DatabaseCutoverResult
{
    private DatabaseCutoverResult() { }

    public sealed record Succeeded(Guid OperationId, string ReceiptPath, bool AlreadyComplete)
        : DatabaseCutoverResult
    {
        public override T Match<T>(
            Func<Succeeded, T> succeeded,
            Func<Rejected, T> rejected,
            Func<Failed, T> failed
        ) => succeeded(this);
    }

    public sealed record Rejected(string Message) : DatabaseCutoverResult
    {
        public override T Match<T>(
            Func<Succeeded, T> succeeded,
            Func<Rejected, T> rejected,
            Func<Failed, T> failed
        ) => rejected(this);
    }

    public sealed record Failed(string Message) : DatabaseCutoverResult
    {
        public override T Match<T>(
            Func<Succeeded, T> succeeded,
            Func<Rejected, T> rejected,
            Func<Failed, T> failed
        ) => failed(this);
    }

    public abstract T Match<T>(
        Func<Succeeded, T> succeeded,
        Func<Rejected, T> rejected,
        Func<Failed, T> failed
    );
}
