using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public static class PluginStructuredValueSchemas
{
    public static PluginLuaFieldDescriptor ActorLogin { get; } =
        Field("login", PluginLuaFieldShape.String, "The actor's normalized login.");
    public static PluginLuaFieldDescriptor ActorDisplayName { get; } =
        Field("displayName", PluginLuaFieldShape.String, "The actor's display name.");
    public static PluginLuaFieldDescriptor ActorTwitchUserId { get; } =
        Field("twitchUserId", PluginLuaFieldShape.String, "The actor's Twitch user ID.", false);
    public static PluginLuaFieldDescriptor ActorIsBroadcaster { get; } =
        Field(
            "isBroadcaster",
            PluginLuaFieldShape.Boolean,
            "Whether the actor is the broadcaster."
        );
    public static PluginLuaFieldDescriptor ActorIsModerator { get; } =
        Field("isModerator", PluginLuaFieldShape.Boolean, "Whether the actor is a moderator.");
    public static PluginLuaFieldDescriptor ActorIsSubscriber { get; } =
        Field("isSubscriber", PluginLuaFieldShape.Boolean, "Whether the actor is a subscriber.");
    public static PluginLuaSchemaDescriptor ActorContext { get; } =
        Schema(
            "BlokeBotActorContext",
            "The admitted channel actor.",
            null,
            ActorLogin,
            ActorDisplayName,
            ActorTwitchUserId,
            ActorIsBroadcaster,
            ActorIsModerator,
            ActorIsSubscriber
        );

    public static PluginLuaFieldDescriptor StreamId { get; } =
        Field("streamId", PluginLuaFieldShape.String, "The current Twitch stream ID.", false);
    public static PluginLuaFieldDescriptor StreamIsLive { get; } =
        Field("isLive", PluginLuaFieldShape.Boolean, "Whether the channel is live.");
    public static PluginLuaSchemaDescriptor StreamContext { get; } =
        Schema("BlokeBotStreamContext", "The admitted stream state.", null, StreamId, StreamIsLive);

    public static PluginLuaFieldDescriptor CommandRoute { get; } =
        PluginInvocationInputSchemas.CommandRoute;
    public static PluginLuaFieldDescriptor CommandArguments { get; } =
        PluginInvocationInputSchemas.CommandArguments;
    public static PluginLuaSchemaDescriptor CommandContext { get; } =
        Schema(
            "BlokeBotCommandContext",
            "The admitted command invocation.",
            null,
            CommandRoute,
            CommandArguments
        );

    public static PluginLuaFieldDescriptor EventHandlerId { get; } =
        Field("handlerId", PluginLuaFieldShape.String, "The declared event handler ID.");
    public static PluginLuaFieldDescriptor EventSource { get; } =
        Field("source", PluginLuaFieldShape.String, "The canonical event source.");
    public static PluginLuaFieldDescriptor EventId { get; } =
        Field("eventId", PluginLuaFieldShape.String, "The stable event correlation ID.");
    public static PluginLuaFieldDescriptor EventOccurredAt { get; } =
        Field("occurredAt", PluginLuaFieldShape.String, "The UTC event timestamp.");
    public static PluginLuaSchemaDescriptor EventContext { get; } =
        Schema(
            "BlokeBotEventContext",
            "The admitted event invocation.",
            null,
            EventHandlerId,
            EventSource,
            EventId,
            EventOccurredAt
        );

    public static PluginLuaFieldDescriptor ScheduleHandlerId { get; } =
        Field("handlerId", PluginLuaFieldShape.String, "The declared schedule handler ID.");
    public static PluginLuaFieldDescriptor ScheduleId { get; } =
        Field("scheduleId", PluginLuaFieldShape.String, "The schedule invocation ID.");
    public static PluginLuaFieldDescriptor ScheduleDueAt { get; } =
        Field("dueAt", PluginLuaFieldShape.String, "The UTC scheduled time.");
    public static PluginLuaSchemaDescriptor ScheduleContext { get; } =
        Schema(
            "BlokeBotScheduleContext",
            "The admitted schedule invocation.",
            null,
            ScheduleHandlerId,
            ScheduleId,
            ScheduleDueAt
        );

    public static PluginLuaFieldDescriptor WebKind { get; } =
        Field(
            "kind",
            new PluginLuaFieldShape.LiteralText("webhook", "action"),
            "The HTTP admission surface."
        );
    public static PluginLuaFieldDescriptor WebRouteId { get; } =
        Field("routeId", PluginLuaFieldShape.String, "The declared webhook or HTTP action ID.");
    public static PluginLuaFieldDescriptor WebMethod { get; } =
        PluginInvocationInputSchemas.WebMethod;
    public static PluginLuaSchemaDescriptor WebContext { get; } =
        Schema(
            "BlokeBotWebContext",
            "The admitted HTTP invocation.",
            null,
            WebKind,
            WebRouteId,
            WebMethod
        );

    public static PluginLuaFieldDescriptor AutomationDefinitionId { get; } =
        Field("definitionId", PluginLuaFieldShape.String, "The automation definition ID.");
    public static PluginLuaFieldDescriptor AutomationInvocationId { get; } =
        Field("invocationId", PluginLuaFieldShape.String, "The automation invocation ID.");
    public static PluginLuaSchemaDescriptor AutomationContext { get; } =
        Schema(
            "BlokeBotAutomationContext",
            "The admitted automation invocation.",
            null,
            AutomationDefinitionId,
            AutomationInvocationId
        );

    public static PluginLuaFieldDescriptor MigrationId { get; } =
        PluginInvocationInputSchemas.MigrationId;
    public static PluginLuaFieldDescriptor MigrationFromVersion { get; } =
        PluginInvocationInputSchemas.MigrationFromVersion;
    public static PluginLuaFieldDescriptor MigrationToVersion { get; } =
        PluginInvocationInputSchemas.MigrationToVersion;
    public static PluginLuaSchemaDescriptor MigrationContext { get; } =
        Schema(
            "BlokeBotMigrationContext",
            "The admitted migration identity.",
            null,
            MigrationId,
            MigrationFromVersion,
            MigrationToVersion
        );

    public static PluginLuaFieldDescriptor PageId { get; } =
        Field("pageId", PluginLuaFieldShape.String, "The declared page ID.");
    public static PluginLuaFieldDescriptor PageSessionId { get; } =
        PluginInvocationInputSchemas.PageSessionId;
    public static PluginLuaSchemaDescriptor PageContext { get; } =
        Schema("BlokeBotPageContext", "The admitted page identity.", null, PageId, PageSessionId);

    public static PluginLuaFieldDescriptor ContextPluginId { get; } =
        Field("pluginId", PluginLuaFieldShape.String, "The invoking plugin ID.");
    public static PluginLuaFieldDescriptor ContextPluginVersion { get; } =
        Field("pluginVersion", PluginLuaFieldShape.String, "The invoking plugin version.");
    public static PluginLuaFieldDescriptor ContextPluginTag { get; } =
        Field("pluginTag", PluginLuaFieldShape.String, "The invoking plugin release tag.");
    public static PluginLuaSchemaDescriptor ContextBase { get; } =
        Schema(
            "BlokeBotContextBase",
            "Fields common to every admitted invocation context.",
            null,
            ContextPluginId,
            ContextPluginVersion,
            ContextPluginTag
        );

    public static PluginLuaFieldDescriptor InstallationKind { get; } =
        Field("kind", new PluginLuaFieldShape.LiteralText("installation"), "The context kind.");
    public static PluginLuaSchemaDescriptor InstallationInvocationContext { get; } =
        Schema(
            "BlokeBotInstallationContext",
            "An installation lifecycle invocation.",
            ContextBase,
            InstallationKind
        );

    public static PluginLuaFieldDescriptor ChannelKind { get; } =
        Field("kind", new PluginLuaFieldShape.LiteralText("channel"), "The context kind.");
    public static PluginLuaFieldDescriptor ContextHostId { get; } =
        PluginInvocationInputSchemas.PageHostId;
    public static PluginLuaFieldDescriptor ContextFeatureId { get; } =
        Field("featureId", PluginLuaFieldShape.String, "The admitted plugin feature ID.");
    public static PluginLuaFieldDescriptor ChannelActor { get; } =
        Field(
            "actor",
            new PluginLuaFieldShape.Structured(ActorContext),
            "The admitted actor.",
            false
        );
    public static PluginLuaFieldDescriptor ChannelStream { get; } =
        Field(
            "stream",
            new PluginLuaFieldShape.Structured(StreamContext),
            "The current stream.",
            false
        );
    public static PluginLuaFieldDescriptor ChannelCommand { get; } =
        Field(
            "command",
            new PluginLuaFieldShape.Structured(CommandContext),
            "The command invocation.",
            false
        );
    public static PluginLuaFieldDescriptor ChannelEvent { get; } =
        Field(
            "event",
            new PluginLuaFieldShape.Structured(EventContext),
            "The event invocation.",
            false
        );
    public static PluginLuaFieldDescriptor ChannelSchedule { get; } =
        Field(
            "schedule",
            new PluginLuaFieldShape.Structured(ScheduleContext),
            "The schedule invocation.",
            false
        );
    public static PluginLuaFieldDescriptor ChannelWeb { get; } =
        Field("web", new PluginLuaFieldShape.Structured(WebContext), "The HTTP invocation.", false);
    public static PluginLuaSchemaDescriptor ChannelInvocationContext { get; } =
        Schema(
            "BlokeBotChannelContext",
            "A channel-scoped invocation.",
            ContextBase,
            ChannelKind,
            ContextHostId,
            ContextFeatureId,
            ChannelActor,
            ChannelStream,
            ChannelCommand,
            ChannelEvent,
            ChannelSchedule,
            ChannelWeb
        );

    public static PluginLuaFieldDescriptor AutomationKind { get; } =
        Field("kind", new PluginLuaFieldShape.LiteralText("automation"), "The context kind.");
    public static PluginLuaFieldDescriptor InvocationAutomation { get; } =
        Field(
            "automation",
            new PluginLuaFieldShape.Structured(AutomationContext),
            "The automation invocation."
        );
    public static PluginLuaSchemaDescriptor AutomationInvocationContext { get; } =
        Schema(
            "BlokeBotAutomationInvocationContext",
            "An automation invocation.",
            ContextBase,
            AutomationKind,
            ContextHostId,
            ContextFeatureId,
            InvocationAutomation
        );

    public static PluginLuaFieldDescriptor MigrationKind { get; } =
        Field("kind", new PluginLuaFieldShape.LiteralText("migration"), "The context kind.");
    public static PluginLuaFieldDescriptor InvocationMigration { get; } =
        Field(
            "migration",
            new PluginLuaFieldShape.Structured(MigrationContext),
            "The migration identity."
        );
    public static PluginLuaSchemaDescriptor MigrationInvocationContext { get; } =
        Schema(
            "BlokeBotMigrationInvocationContext",
            "A migration invocation.",
            ContextBase,
            MigrationKind,
            InvocationMigration
        );

    public static PluginLuaFieldDescriptor PageKind { get; } =
        Field("kind", new PluginLuaFieldShape.LiteralText("page"), "The context kind.");
    public static PluginLuaFieldDescriptor InvocationPage { get; } =
        Field("page", new PluginLuaFieldShape.Structured(PageContext), "The page identity.");
    public static PluginLuaSchemaDescriptor PageInvocationContext { get; } =
        Schema(
            "BlokeBotPageInvocationContext",
            "A plugin page invocation.",
            ContextBase,
            PageKind,
            ContextHostId,
            ContextFeatureId,
            InvocationPage
        );

    public static PluginLuaFieldDescriptor HttpRequestMethod { get; } =
        Field(
            "method",
            new PluginLuaFieldShape.LiteralText("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"),
            "The HTTP method."
        );
    public static PluginLuaFieldDescriptor HttpRequestUrl { get; } =
        Field("url", PluginLuaFieldShape.String, "The absolute HTTP or HTTPS URL.");
    public static PluginLuaFieldDescriptor HttpRequestHeaders { get; } =
        Field("headers", PluginLuaFieldShape.StringMap, "Optional request headers.", false);
    public static PluginLuaFieldDescriptor HttpRequestBody { get; } =
        Field("body", PluginLuaFieldShape.String, "Optional UTF-8 request body.", false);
    public static PluginLuaSchemaDescriptor HttpRequest { get; } =
        Schema(
            "BlokeBotHttpRequest",
            "An outbound HTTP request.",
            null,
            HttpRequestMethod,
            HttpRequestUrl,
            HttpRequestHeaders,
            HttpRequestBody
        );

    public static PluginLuaFieldDescriptor HttpResponseKind { get; } =
        Field("kind", new PluginLuaFieldShape.LiteralText("response"), "The HTTP outcome kind.");
    public static PluginLuaFieldDescriptor HttpResponseStatus { get; } =
        Field("status", PluginLuaFieldShape.WholeNumber, "The HTTP response status.");
    public static PluginLuaFieldDescriptor HttpResponseHeaders { get; } =
        Field("headers", PluginLuaFieldShape.StringMap, "The response headers.");
    public static PluginLuaFieldDescriptor HttpResponseBodyBase64 { get; } =
        Field("bodyBase64", PluginLuaFieldShape.String, "The response body encoded as base64.");
    public static PluginLuaSchemaDescriptor HttpResponse { get; } =
        Schema(
            "BlokeBotHttpResponse",
            "A completed HTTP response.",
            null,
            HttpResponseKind,
            HttpResponseStatus,
            HttpResponseHeaders,
            HttpResponseBodyBase64
        );

    public static PluginLuaFieldDescriptor HttpRejectedKind { get; } =
        Field("kind", new PluginLuaFieldShape.LiteralText("rejected"), "The HTTP outcome kind.");
    public static PluginLuaFieldDescriptor HttpRejectedCode { get; } =
        Field("code", PluginLuaFieldShape.String, "The stable HTTP rejection code.");
    public static PluginLuaSchemaDescriptor HttpRejected { get; } =
        Schema(
            "BlokeBotHttpRejected",
            "An HTTP request rejected before transport.",
            null,
            HttpRejectedKind,
            HttpRejectedCode
        );

    public static PluginLuaFieldDescriptor HttpFailedKind { get; } =
        Field("kind", new PluginLuaFieldShape.LiteralText("failed"), "The HTTP outcome kind.");
    public static PluginLuaFieldDescriptor HttpFailedCode { get; } =
        Field("code", PluginLuaFieldShape.String, "The stable HTTP failure code.");
    public static PluginLuaSchemaDescriptor HttpFailed { get; } =
        Schema(
            "BlokeBotHttpFailed",
            "An HTTP transport failure.",
            null,
            HttpFailedKind,
            HttpFailedCode
        );

    public static PluginLuaUnionDescriptor Context { get; } =
        new(
            "BlokeBotContext",
            "The exact admitted invocation context.",
            [
                InstallationInvocationContext,
                ChannelInvocationContext,
                AutomationInvocationContext,
                MigrationInvocationContext,
                PageInvocationContext,
            ]
        );

    public static PluginLuaUnionDescriptor HttpOutcome { get; } =
        new(
            "BlokeBotHttpOutcome",
            "The typed outbound HTTP outcome.",
            [HttpResponse, HttpRejected, HttpFailed]
        );

    public static ImmutableArray<PluginLuaSchemaDescriptor> All { get; } =
    [
        ActorContext,
        StreamContext,
        CommandContext,
        EventContext,
        ScheduleContext,
        WebContext,
        AutomationContext,
        MigrationContext,
        PageContext,
        ContextBase,
        InstallationInvocationContext,
        ChannelInvocationContext,
        AutomationInvocationContext,
        MigrationInvocationContext,
        PageInvocationContext,
        HttpRequest,
        HttpResponse,
        HttpRejected,
        HttpFailed,
    ];

    public static ImmutableArray<PluginLuaUnionDescriptor> Unions { get; } = [Context, HttpOutcome];

    private static PluginLuaFieldDescriptor Field(
        string name,
        PluginLuaFieldShape shape,
        string description,
        bool required = true
    ) => new(name, shape, description, required);

    private static PluginLuaSchemaDescriptor Schema(
        string luaTypeName,
        string description,
        PluginLuaSchemaDescriptor? baseSchema,
        params ReadOnlySpan<PluginLuaFieldDescriptor> fields
    ) => new(luaTypeName, description, [.. fields], baseSchema);
}
