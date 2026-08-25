using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeLifecycleService(
    HostedChannelRuntimeTransitionService runtimeTransitions
)
{
    public async Task RecoverInterruptedStopsAsync(CancellationToken ct) =>
        _ = await runtimeTransitions.RecoverInterruptedStopsAsync(ct);

    public async Task<bool> MarkStartedAsync(BotChannelTarget target, CancellationToken ct)
    {
        var normalized = LoginName.Parse(target.Channel);
        return !normalized.IsEmpty
            && await runtimeTransitions.ConfirmStartedAsync(
                normalized.Value,
                target.SessionIdentity,
                ct
            );
    }

    public async Task<bool> MarkStoppedAsync(BotChannelTarget target, CancellationToken ct)
    {
        var normalized = LoginName.Parse(target.Channel);
        return !normalized.IsEmpty
            && await runtimeTransitions.ConfirmStoppedAsync(
                normalized.Value,
                target.SessionIdentity,
                ct
            );
    }
}
