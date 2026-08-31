using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public static class PluginInvocationInputSchemas
{
    public static PluginInvocationInputFieldDescriptor CommandRoute { get; } =
        Field("route", PluginInvocationInputFieldShape.String, "The normalized command route.");

    public static PluginInvocationInputFieldDescriptor CommandArguments { get; } =
        Field(
            "arguments",
            PluginInvocationInputFieldShape.StringArray,
            "The ordered command arguments."
        );

    public static PluginInvocationInputSchemaDescriptor Command { get; } =
        Schema(
            "BlokeBotCommandInput",
            "Input delivered to a declared command handler.",
            CommandRoute,
            CommandArguments
        );

    public static PluginInvocationInputFieldDescriptor WebMethod { get; } =
        Field("method", PluginInvocationInputFieldShape.String, "The uppercase HTTP method.");

    public static PluginInvocationInputFieldDescriptor WebHeaders { get; } =
        Field(
            "headers",
            PluginInvocationInputFieldShape.StringMap,
            "The request headers with lowercase names."
        );

    public static PluginInvocationInputFieldDescriptor WebBodyBase64 { get; } =
        Field(
            "bodyBase64",
            PluginInvocationInputFieldShape.String,
            "The request body encoded as base64."
        );

    public static PluginInvocationInputSchemaDescriptor Web { get; } =
        Schema(
            "BlokeBotWebInput",
            "Input delivered to declared webhook, action, and webhook authentication handlers.",
            WebMethod,
            WebHeaders,
            WebBodyBase64
        );

    public static PluginInvocationInputFieldDescriptor PageVersion { get; } =
        Field(
            "version",
            PluginInvocationInputFieldShape.WholeNumber,
            "The generated page input version."
        );

    public static PluginInvocationInputFieldDescriptor PageHostId { get; } =
        Field(
            "hostId",
            PluginInvocationInputFieldShape.WholeNumber,
            "The selected BlokeBot host ID."
        );

    public static PluginInvocationInputFieldDescriptor PageSessionId { get; } =
        Field(
            "sessionId",
            PluginInvocationInputFieldShape.String,
            "The generated page session ID."
        );

    public static PluginInvocationInputSchemaDescriptor Page { get; } =
        Schema(
            "BlokeBotPageInput",
            "Input delivered to a generated page renderer.",
            PageVersion,
            PageHostId,
            PageSessionId
        );

    public static PluginInvocationInputFieldDescriptor EventId { get; } =
        Field(
            "event_id",
            PluginInvocationInputFieldShape.String,
            "The stable event correlation ID."
        );

    public static PluginInvocationInputFieldDescriptor EventSource { get; } =
        Field("source", PluginInvocationInputFieldShape.String, "The canonical event source name.");

    public static PluginInvocationInputSchemaDescriptor BlokeBotEvent { get; } =
        Schema(
            "BlokeBotEventInput",
            "Input delivered for a declared BlokeBot event.",
            EventId,
            EventSource
        );

    public static PluginInvocationInputFieldDescriptor EventOccurredAt { get; } =
        Field(
            "occurred_at",
            PluginInvocationInputFieldShape.String,
            "The UTC event timestamp in round-trip format."
        );

    public static PluginInvocationInputSchemaDescriptor TwitchEvent { get; } =
        Schema(
            "BlokeBotTwitchEventInput",
            "Input delivered for a declared typed Twitch event.",
            EventId,
            EventSource,
            EventOccurredAt
        );

    public static PluginInvocationInputFieldDescriptor RawSubscriptionType { get; } =
        Field("type", PluginInvocationInputFieldShape.String, "The EventSub subscription type.");

    public static PluginInvocationInputFieldDescriptor RawSubscriptionVersion { get; } =
        Field(
            "version",
            PluginInvocationInputFieldShape.String,
            "The EventSub subscription version."
        );

    public static PluginInvocationInputSchemaDescriptor TwitchRawSubscription { get; } =
        Schema(
            "BlokeBotTwitchRawSubscription",
            "Subscription identity included with a raw Twitch event.",
            RawSubscriptionType,
            RawSubscriptionVersion
        );

    public static PluginInvocationInputFieldDescriptor RawSubscription { get; } =
        Field(
            "subscription",
            new PluginInvocationInputFieldShape.Structured(TwitchRawSubscription),
            "The raw EventSub subscription identity."
        );

    public static PluginInvocationInputFieldDescriptor RawEvent { get; } =
        Field("event", PluginInvocationInputFieldShape.Map, "The raw EventSub event payload.");

    public static PluginInvocationInputSchemaDescriptor TwitchRawEvent { get; } =
        Schema(
            "BlokeBotTwitchRawEventInput",
            "Input delivered for a declared raw Twitch EventSub event.",
            RawSubscription,
            RawEvent
        );

    public static ImmutableArray<PluginInvocationInputSchemaDescriptor> All { get; } =
    [Command, Web, Page, BlokeBotEvent, TwitchEvent, TwitchRawSubscription, TwitchRawEvent];

    private static PluginInvocationInputFieldDescriptor Field(
        string name,
        PluginInvocationInputFieldShape shape,
        string description
    ) => new(name, shape, description);

    private static PluginInvocationInputSchemaDescriptor Schema(
        string luaTypeName,
        string description,
        params ReadOnlySpan<PluginInvocationInputFieldDescriptor> fields
    ) => new(luaTypeName, description, [.. fields]);
}
