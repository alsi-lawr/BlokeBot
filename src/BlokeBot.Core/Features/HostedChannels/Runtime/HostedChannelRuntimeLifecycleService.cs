using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeLifecycleService(
    HostedChannelRuntimeTransitionService runtimeTransitions
)
{
    public async Task RecoverInterruptedStopsAsync(CancellationToken ct) =>
        _ = await runtimeTransitions.RecoverInterruptedStopsAsync(ct);

    public async Task MarkStartedAsync(string channel, CancellationToken ct)
    {
        var normalized = LoginName.Parse(channel);
        if (normalized.IsEmpty)
        {
            return;
        }

        _ = await runtimeTransitions.ConfirmStartedAsync(normalized.Value, ct);
    }

    public async Task MarkStoppedAsync(string channel, CancellationToken ct)
    {
        var normalized = LoginName.Parse(channel);
        if (normalized.IsEmpty)
        {
            return;
        }

        _ = await runtimeTransitions.ConfirmStoppedAsync(normalized.Value, ct);
    }
}
