namespace BlokeBot.Core.Features.ViewerPortal;

internal static class PortalProjectionRunner
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    internal static async Task<PortalSummaryOutcome> ReadAsync(
        Func<CancellationToken, Task<PortalSummaryOutcome>> read,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            return await read(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PortalSummaryOutcome.Unavailable();
        }
    }
}
