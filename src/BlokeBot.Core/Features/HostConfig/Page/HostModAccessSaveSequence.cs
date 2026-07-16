using BlokeBot.Core.Features.HostConfig.Access;

namespace BlokeBot.Core.Features.HostConfig.Page;

internal sealed class HostModAccessSaveSequence : IDisposable
{
    private CancellationTokenSource? _currentCancellation;
    private int _currentVersion;

    public bool HasPendingSubmission => _currentCancellation is not null;

    public HostModAccessSaveSubmission Begin(
        HostModAccessSaveCommand command,
        HostModAccessState previousAccess
    )
    {
        _currentCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _currentCancellation = cancellation;
        return new(++_currentVersion, command, previousAccess, cancellation);
    }

    public bool IsCurrent(HostModAccessSaveSubmission submission)
    {
        return submission.Version == _currentVersion
            && ReferenceEquals(_currentCancellation, submission.Cancellation);
    }

    public void Complete(HostModAccessSaveSubmission submission)
    {
        if (IsCurrent(submission))
        {
            _currentCancellation = null;
        }

        submission.Dispose();
    }

    public void Dispose()
    {
        _currentCancellation?.Cancel();
        _currentCancellation = null;
    }
}

internal sealed class HostModAccessSaveSubmission : IDisposable
{
    internal HostModAccessSaveSubmission(
        int version,
        HostModAccessSaveCommand command,
        HostModAccessState previousAccess,
        CancellationTokenSource cancellation
    )
    {
        Version = version;
        Command = command;
        PreviousAccess = previousAccess;
        Cancellation = cancellation;
    }

    public int Version { get; }

    public HostModAccessSaveCommand Command { get; }

    public HostModAccessState PreviousAccess { get; }

    public CancellationToken CancellationToken => Cancellation.Token;

    internal CancellationTokenSource Cancellation { get; }

    public void Dispose()
    {
        Cancellation.Dispose();
    }
}
