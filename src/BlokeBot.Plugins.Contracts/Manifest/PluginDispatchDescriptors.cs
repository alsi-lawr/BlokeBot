using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginDispatchDeclarations
{
    public PluginDispatchDeclarations(
        ImmutableArray<PluginCommandDescriptor> commands,
        ImmutableArray<PluginEventHandlerDescriptor> events,
        ImmutableArray<PluginScheduleHandlerDescriptor> schedules,
        ImmutableArray<PluginWebhookDescriptor> webhooks = default,
        ImmutableArray<PluginActionDescriptor> actions = default
    )
    {
        Commands = commands;
        Events = events;
        Schedules = schedules;
        Webhooks = webhooks.IsDefault ? [] : webhooks;
        Actions = actions.IsDefault ? [] : actions;
    }

    public ImmutableArray<PluginCommandDescriptor> Commands { get; init; }

    public ImmutableArray<PluginEventHandlerDescriptor> Events { get; init; }

    public ImmutableArray<PluginScheduleHandlerDescriptor> Schedules { get; init; }

    public ImmutableArray<PluginWebhookDescriptor> Webhooks { get; init; }

    public ImmutableArray<PluginActionDescriptor> Actions { get; init; }

    public void Deconstruct(
        out ImmutableArray<PluginCommandDescriptor> commands,
        out ImmutableArray<PluginEventHandlerDescriptor> events,
        out ImmutableArray<PluginScheduleHandlerDescriptor> schedules
    )
    {
        commands = Commands;
        events = Events;
        schedules = Schedules;
    }

    public static PluginDispatchDeclarations Empty { get; } = new([], [], [], [], []);
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PluginWebhookAuthentication.Public), "public")]
[JsonDerivedType(typeof(PluginWebhookAuthentication.Callback), "callback")]
public abstract record PluginWebhookAuthentication
{
    private PluginWebhookAuthentication() { }

    public sealed record Public : PluginWebhookAuthentication;

    public sealed record Callback(PluginLuaModuleId Module, PluginHostOperationId Operation)
        : PluginWebhookAuthentication;
}

public sealed record PluginWebhookDescriptor(
    PluginWebhookId Id,
    PluginLuaModuleId Module,
    PluginHostOperationId Operation,
    PluginCallbackRequirements Requirements,
    PluginWebhookAuthentication Authentication
);

public sealed record PluginActionDescriptor(
    PluginActionId Id,
    PluginLuaModuleId Module,
    PluginHostOperationId Operation,
    PluginCallbackRequirements Requirements
);
