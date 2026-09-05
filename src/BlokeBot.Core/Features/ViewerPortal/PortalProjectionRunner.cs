using System.Diagnostics;
using System.Text.Json;

namespace BlokeBot.Core.Features.ViewerPortal;

internal sealed class PortalProjectionRunner(PortalReadTelemetry telemetry)
{
    internal const int MaximumSummaryBytes = 4096;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    internal async Task<PortalSummaryOutcome> ReadAsync(
        int hostId,
        PortalFeatureDescriptor descriptor,
        Func<CancellationToken, Task<PortalSummaryOutcome>> read,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = telemetry.Start(descriptor.Icon, descriptor.Audience);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var outcome = PortalReadOutcome.Unavailable;
        try
        {
            var result = await read(timeout.Token).WaitAsync(timeout.Token);
            var bytes = result.Match(
                value => JsonSerializer.SerializeToUtf8Bytes(value.Summary).Length,
                value => JsonSerializer.SerializeToUtf8Bytes(value.Summary).Length,
                _ => 0,
                value => JsonSerializer.SerializeToUtf8Bytes(value.Summary).Length,
                _ => 0,
                _ => 0
            );
            if (bytes > MaximumSummaryBytes)
            {
                outcome = PortalReadOutcome.BudgetExceeded;
                return new PortalSummaryOutcome.Unavailable();
            }
            outcome = result.Match(
                _ => PortalReadOutcome.Available,
                _ => PortalReadOutcome.Empty,
                _ => PortalReadOutcome.Disabled,
                _ => PortalReadOutcome.Degraded,
                _ => PortalReadOutcome.Unavailable,
                _ => PortalReadOutcome.Unauthorized
            );
            return result;
        }
        catch (Exception)
        {
            outcome = cancellationToken.IsCancellationRequested
                ? PortalReadOutcome.Cancelled
                : PortalReadOutcome.Unavailable;
            cancellationToken.ThrowIfCancellationRequested();
            return new PortalSummaryOutcome.Unavailable();
        }
        finally
        {
            _ = activity?.SetTag("portal.outcome", outcome.ToString());
            await telemetry.ObserveAsync(
                hostId,
                descriptor.Icon,
                descriptor.Audience,
                outcome,
                Stopwatch.GetElapsedTime(started)
            );
        }
    }
}
