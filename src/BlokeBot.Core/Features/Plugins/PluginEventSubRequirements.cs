using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginEventSubRequirementSource(
    IPluginHostContextResolver hosts,
    IPluginFeatureDeclarationProvider declarations,
    IPluginFeatureSnapshotProvider features,
    IPluginRuntimeSnapshotProvider runtime
) : IEventSubRequirementSource, IEventSubExactRequirementSource
{
    public async ValueTask<IReadOnlyList<EventSubExactSubscription>> GetRequirementsAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        var host = await hosts.FindAsync(channel, cancellationToken);
        return host is null
            ? []
            : CurrentFeatures(host.Id)
                .SelectMany(static feature => feature.DispatchDeclarations.Events)
                .Select(static handler => handler.Source)
                .OfType<PluginEventSource.TwitchRaw>()
                .Select(static source => new EventSubExactSubscription(
                    source.EventSubType,
                    source.Version
                ))
                .Distinct()
                .OrderBy(static requirement => requirement.Type, StringComparer.Ordinal)
                .ThenBy(static requirement => requirement.Version, StringComparer.Ordinal)
                .ToArray();
    }

    public async ValueTask<bool> RequiresAsync(
        string channel,
        AutomationEventSubRequirement requirement,
        CancellationToken cancellation
    )
    {
        var host = await hosts.FindAsync(channel, cancellation);
        if (host is null)
        {
            return false;
        }
        var requiredTypes = EventTypes(requirement);
        return requiredTypes.Count != 0
            && CurrentFeatures(host.Id)
                .Any(feature => feature.Twitch.EventSubTypes.Any(requiredTypes.Contains));
    }

    private IEnumerable<PluginFeatureDescriptor> CurrentFeatures(PluginHostId hostId) =>
        features
            .Current.States.Values.Where(state =>
                state.Key.HostId == hostId
                && state.Enabled
                && declarations.Current.Declarations.TryGetValue(
                    state.Key.PluginId,
                    out var declaration
                )
                && declaration.Fence == state.Fence
                && runtime.Current.Entries.TryGetValue(state.Key.PluginId, out var entry)
                && entry.Fence == state.Fence
                && entry.Phase == PluginLifecyclePhase.Active
            )
            .Select(state =>
                declarations
                    .Current.Declarations[state.Key.PluginId]
                    .FindFeature(state.Key.FeatureId)
            )
            .OfType<PluginFeatureDescriptor>();

    private static IReadOnlySet<string> EventTypes(AutomationEventSubRequirement requirement) =>
        requirement switch
        {
            AutomationEventSubRequirement.Stream => Set("stream.online", "stream.offline"),
            AutomationEventSubRequirement.Follows => Set("channel.follow"),
            AutomationEventSubRequirement.Subscriptions => Set(
                "channel.subscribe",
                "channel.subscription.gift"
            ),
            AutomationEventSubRequirement.Cheers => Set("channel.cheer"),
            AutomationEventSubRequirement.HypeTrain => Set(
                "channel.hype_train.begin",
                "channel.hype_train.progress",
                "channel.hype_train.end"
            ),
            AutomationEventSubRequirement.ChatNotifications => Set("channel.chat.notification"),
            AutomationEventSubRequirement.ChannelUpdates => Set("channel.update"),
            AutomationEventSubRequirement.IncomingRaids => Set("channel.raid"),
            AutomationEventSubRequirement.Redemptions => Set(
                "channel.channel_points_custom_reward_redemption.add",
                "channel.channel_points_custom_reward_redemption.update"
            ),
            AutomationEventSubRequirement.Shoutouts => Set(
                "channel.shoutout.create",
                "channel.shoutout.receive"
            ),
            AutomationEventSubRequirement.Polls => Set(
                "channel.poll.begin",
                "channel.poll.progress",
                "channel.poll.end"
            ),
            AutomationEventSubRequirement.Predictions => Set(
                "channel.prediction.begin",
                "channel.prediction.progress",
                "channel.prediction.lock",
                "channel.prediction.end"
            ),
            _ => throw new InvalidOperationException("Unknown EventSub requirement."),
        };

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
