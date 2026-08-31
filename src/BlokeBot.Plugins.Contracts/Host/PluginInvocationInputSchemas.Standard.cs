using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public static class PluginInvocationInputSchemas
{
    public static PluginLuaFieldDescriptor CommandRoute { get; } =
        Field("route", PluginLuaFieldShape.String, "The normalized command route.");

    public static PluginLuaFieldDescriptor CommandArguments { get; } =
        Field("arguments", PluginLuaFieldShape.StringArray, "The ordered command arguments.");

    public static PluginLuaSchemaDescriptor Command { get; } =
        Schema(
            "BlokeBotCommandInput",
            "Input delivered to a declared command handler.",
            CommandRoute,
            CommandArguments
        );

    public static PluginLuaFieldDescriptor WebMethod { get; } =
        Field("method", PluginLuaFieldShape.String, "The uppercase HTTP method.");

    public static PluginLuaFieldDescriptor WebHeaders { get; } =
        Field(
            "headers",
            PluginLuaFieldShape.StringMap,
            "The request headers with lowercase names."
        );

    public static PluginLuaFieldDescriptor WebBodyBase64 { get; } =
        Field("bodyBase64", PluginLuaFieldShape.String, "The request body encoded as base64.");

    public static PluginLuaSchemaDescriptor Web { get; } =
        Schema(
            "BlokeBotWebInput",
            "Input delivered to declared webhook, HTTP action, and webhook authentication handlers.",
            WebMethod,
            WebHeaders,
            WebBodyBase64
        );

    public static PluginLuaFieldDescriptor PageVersion { get; } =
        Field("version", PluginLuaFieldShape.WholeNumber, "The generated page input version.");

    public static PluginLuaFieldDescriptor PageHostId { get; } =
        Field("hostId", PluginLuaFieldShape.WholeNumber, "The selected BlokeBot host ID.");

    public static PluginLuaFieldDescriptor PageSessionId { get; } =
        Field("sessionId", PluginLuaFieldShape.String, "The generated page session ID.");

    public static PluginLuaSchemaDescriptor Page { get; } =
        Schema(
            "BlokeBotPageInput",
            "Input delivered to a generated page renderer.",
            PageVersion,
            PageHostId,
            PageSessionId
        );

    public static PluginLuaFieldDescriptor EventId { get; } =
        Field("event_id", PluginLuaFieldShape.String, "The stable event correlation ID.");

    public static PluginLuaFieldDescriptor EventSource { get; } =
        Field("source", PluginLuaFieldShape.String, "The canonical event source name.");

    public static PluginLuaSchemaDescriptor BlokeBotEvent { get; } =
        Schema(
            "BlokeBotEventInput",
            "Input delivered for a declared BlokeBot event.",
            EventId,
            EventSource
        );

    public static PluginLuaFieldDescriptor EventOccurredAt { get; } =
        Field(
            "occurred_at",
            PluginLuaFieldShape.String,
            "The UTC event timestamp in round-trip format."
        );

    public static PluginLuaSchemaDescriptor TwitchEvent { get; } =
        Schema(
            "BlokeBotTwitchEventInput",
            "Input delivered for a declared typed Twitch event.",
            EventId,
            EventSource,
            EventOccurredAt
        );

    public static PluginLuaFieldDescriptor RawSubscriptionType { get; } =
        Field("type", PluginLuaFieldShape.String, "The EventSub subscription type.");

    public static PluginLuaFieldDescriptor RawSubscriptionVersion { get; } =
        Field("version", PluginLuaFieldShape.String, "The EventSub subscription version.");

    public static PluginLuaSchemaDescriptor TwitchRawSubscription { get; } =
        Schema(
            "BlokeBotTwitchRawSubscription",
            "Subscription identity included with a raw Twitch event.",
            RawSubscriptionType,
            RawSubscriptionVersion
        );

    public static PluginLuaFieldDescriptor RawSubscription { get; } =
        Field(
            "subscription",
            new PluginLuaFieldShape.Structured(TwitchRawSubscription),
            "The raw EventSub subscription identity."
        );

    public static PluginLuaFieldDescriptor RawEvent { get; } =
        Field("event", PluginLuaFieldShape.Map, "The raw EventSub event payload.");

    public static PluginLuaSchemaDescriptor TwitchRawEvent { get; } =
        Schema(
            "BlokeBotTwitchRawEventInput",
            "Input delivered for a declared raw Twitch EventSub event.",
            RawSubscription,
            RawEvent
        );

    public static PluginLuaFieldDescriptor MigrationId { get; } =
        Field("migrationId", PluginLuaFieldShape.String, "The declared migration ID.");

    public static PluginLuaFieldDescriptor MigrationFromVersion { get; } =
        Field("fromVersion", PluginLuaFieldShape.String, "The source semantic version.");

    public static PluginLuaFieldDescriptor MigrationToVersion { get; } =
        Field("toVersion", PluginLuaFieldShape.String, "The target semantic version.");

    public static PluginLuaSchemaDescriptor Migration { get; } =
        Schema(
            "BlokeBotMigrationInput",
            "Input delivered to a declared migration handler.",
            MigrationId,
            MigrationFromVersion,
            MigrationToVersion
        );

    public static ImmutableArray<PluginLuaSchemaDescriptor> All { get; } =
    [
        Command,
        Web,
        Page,
        BlokeBotEvent,
        TwitchEvent,
        TwitchRawSubscription,
        TwitchRawEvent,
        Migration,
    ];

    private static PluginLuaFieldDescriptor Field(
        string name,
        PluginLuaFieldShape shape,
        string description
    ) => new(name, shape, description);

    private static PluginLuaSchemaDescriptor Schema(
        string luaTypeName,
        string description,
        params ReadOnlySpan<PluginLuaFieldDescriptor> fields
    ) => new(luaTypeName, description, [.. fields]);
}
