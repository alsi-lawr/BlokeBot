using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginFeatureEventSubReconciler(
    IPluginHostContextResolver hosts,
    IHostBotAccountTokenStatusProvider tokens,
    IEventSubChannelReconciliationTrigger eventSub,
    IEventSubChannelStatusAccessor? statuses = null
) : IPluginFeatureReconciler
{
    public async ValueTask<PluginFeatureReconciliationResult> ReconcileAsync(
        PluginFeatureReconciliationRequest request,
        CancellationToken cancellationToken
    )
    {
        var host = await hosts.FindAsync(request.Key.HostId, cancellationToken);
        if (host is null)
        {
            return Failed("The selected channel is unavailable.");
        }
        var token = await tokens.GetActiveTokenStatusAsync(
            host.Login,
            request.Requirements.Scopes,
            cancellationToken
        );
        var scopeResult = token.Status.Match<PluginFeatureReconciliationResult?>(
            _ => Missing(request.Requirements.Scopes),
            _ => Missing(request.Requirements.Scopes),
            _ => Missing(request.Requirements.Scopes),
            missing => new PluginFeatureReconciliationResult.MissingScopes(missing.Missing),
            _ => null
        );
        if (scopeResult is not null)
        {
            return scopeResult;
        }
        if (request.Requirements.EventSubTypes.IsEmpty)
        {
            return new PluginFeatureReconciliationResult.Ready();
        }

        await eventSub.ReconcileAsync(cancellationToken);
        return statuses?.Current.Channels.SingleOrDefault(channel =>
            string.Equals(channel.Channel, host.Login, StringComparison.OrdinalIgnoreCase)
        ) switch
        {
            EventSubChannelStatus.Healthy => new PluginFeatureReconciliationResult.Ready(),
            EventSubChannelStatus.Recovering => new PluginFeatureReconciliationResult.Pending(),
            EventSubChannelStatus.Degraded => Failed("Twitch event setup did not complete."),
            _ => new PluginFeatureReconciliationResult.Pending(),
        };
    }

    public ValueTask CancelAsync(
        PluginFeatureKey key,
        BlokeBot.Plugins.Runtime.PluginLifecycleFence fence,
        PluginFeatureGeneration generation,
        CancellationToken cancellationToken
    ) => new(eventSub.ReconcileAsync(cancellationToken));

    private static PluginFeatureReconciliationResult Missing(
        System.Collections.Immutable.ImmutableArray<string> scopes
    ) =>
        scopes.IsEmpty
            ? Failed("Twitch is unavailable.")
            : new PluginFeatureReconciliationResult.MissingScopes(scopes);

    private static PluginFeatureReconciliationResult.Failed Failed(string message) =>
        new(
            PluginReadinessReason.TryCreate(
                PluginReadinessReasonCode.ReconciliationFailed,
                PluginRecoveryAction.Retry,
                message,
                out var reason
            )
                ? reason
                : throw new InvalidOperationException("Invalid built-in readiness reason.")
        );
}
