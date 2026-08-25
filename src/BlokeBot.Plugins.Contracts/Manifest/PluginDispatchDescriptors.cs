using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginDispatchDeclarations(
    ImmutableArray<PluginCommandDescriptor> Commands,
    ImmutableArray<PluginEventHandlerDescriptor> Events,
    ImmutableArray<PluginScheduleHandlerDescriptor> Schedules
)
{
    public static PluginDispatchDeclarations Empty { get; } = new([], [], []);
}

public sealed record PluginCallbackRequirements(bool TwitchReady)
{
    public static PluginCallbackRequirements Independent { get; } = new(false);

    public static PluginCallbackRequirements Twitch { get; } = new(true);
}

public sealed record PluginCommandDescriptor(
    string Route,
    PluginLuaModuleId Module,
    PluginHostOperationId Operation,
    PluginCallbackRequirements Requirements
);

public enum PluginTwitchEventKind
{
    StreamOnline,
    StreamOffline,
    ChannelUpdated,
    FollowReceived,
    SubscriptionReceived,
    SubscriptionGiftReceived,
    CheerReceived,
    IncomingRaidReceived,
    HypeTrainChanged,
    ChatNotificationReceived,
    RewardRedemptionReceived,
    ShoutoutOccurred,
    PollChanged,
    PredictionChanged,
}

internal static class PluginTwitchEventRequirements
{
    public static ImmutableArray<string> EventSubTypes(PluginTwitchEventKind kind) =>
        kind switch
        {
            PluginTwitchEventKind.StreamOnline => ["stream.online"],
            PluginTwitchEventKind.StreamOffline => ["stream.offline"],
            PluginTwitchEventKind.ChannelUpdated => ["channel.update"],
            PluginTwitchEventKind.FollowReceived => ["channel.follow"],
            PluginTwitchEventKind.SubscriptionReceived => ["channel.subscribe"],
            PluginTwitchEventKind.SubscriptionGiftReceived => ["channel.subscription.gift"],
            PluginTwitchEventKind.CheerReceived => ["channel.cheer"],
            PluginTwitchEventKind.IncomingRaidReceived => ["channel.raid"],
            PluginTwitchEventKind.HypeTrainChanged =>
            [
                "channel.hype_train.begin",
                "channel.hype_train.progress",
                "channel.hype_train.end",
            ],
            PluginTwitchEventKind.ChatNotificationReceived => ["channel.chat.notification"],
            PluginTwitchEventKind.RewardRedemptionReceived =>
            [
                "channel.channel_points_custom_reward_redemption.add",
                "channel.channel_points_custom_reward_redemption.update",
            ],
            PluginTwitchEventKind.ShoutoutOccurred =>
            [
                "channel.shoutout.create",
                "channel.shoutout.receive",
            ],
            PluginTwitchEventKind.PollChanged =>
            [
                "channel.poll.begin",
                "channel.poll.progress",
                "channel.poll.end",
            ],
            PluginTwitchEventKind.PredictionChanged =>
            [
                "channel.prediction.begin",
                "channel.prediction.progress",
                "channel.prediction.lock",
                "channel.prediction.end",
            ],
        };

    public static bool IsTypedEventSubType(string eventSubType) =>
        eventSubType == "channel.chat.message"
        || Enum.GetValues<PluginTwitchEventKind>()
            .Any(kind => EventSubTypes(kind).Contains(eventSubType, StringComparer.Ordinal));
}

public enum PluginBlokeBotEventKind
{
    HostedChannelsChanged,
    GuessingChanged,
    PointsChanged,
    OverlaysChanged,
    TwitchOperationsChanged,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PluginEventSource.Twitch), "twitch")]
[JsonDerivedType(typeof(PluginEventSource.TwitchRaw), "twitchRaw")]
[JsonDerivedType(typeof(PluginEventSource.BlokeBot), "blokeBot")]
public abstract record PluginEventSource
{
    private PluginEventSource() { }

    public sealed record Twitch(PluginTwitchEventKind Kind) : PluginEventSource;

    public sealed record TwitchRaw(string EventSubType, string Version) : PluginEventSource;

    public sealed record BlokeBot(PluginBlokeBotEventKind Kind) : PluginEventSource;
}

public sealed record PluginEventHandlerDescriptor(
    PluginEventHandlerId Id,
    PluginEventSource Source,
    PluginLuaModuleId Module,
    PluginHostOperationId Operation,
    PluginCallbackRequirements Requirements
);

public sealed record PluginScheduleHandlerDescriptor(
    PluginScheduleHandlerId Id,
    PluginLuaModuleId Module,
    PluginHostOperationId Operation,
    PluginCallbackRequirements Requirements
);
