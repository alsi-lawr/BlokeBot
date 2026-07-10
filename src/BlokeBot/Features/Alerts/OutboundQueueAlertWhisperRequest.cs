namespace BlokeBot.Features.Alerts;

internal sealed record OutboundQueueAlertWhisperRequest(
    int HostId,
    string HostLogin,
    string? HostTwitchUserId,
    int PendingCount,
    TimeSpan OldestPendingAge
);

internal interface IOutboundQueueAlertWhisperSender
{
    Task TrySendAsync(OutboundQueueAlertWhisperRequest request, CancellationToken ct);
}
