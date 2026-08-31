using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public static partial class PluginStandardHostModules
{
    public static PluginHostModuleDescriptor Context { get; } =
        Module(
            "context",
            Operation(
                "current",
                "current",
                "Returns the admitted invocation context. Plugin and feature identities cannot be supplied by the caller.",
                AllContexts(),
                [],
                PluginLuaValueShape.Context,
                "The exact installation, channel, automation, migration, or page context."
            )
        );

    public static PluginHostModuleDescriptor Settings { get; } =
        Module(
            "settings",
            Operation(
                "installation",
                "installation",
                "Returns configured installation settings declared by this plugin.",
                LiveContexts(),
                [],
                PluginLuaValueShape.InstallationSettings,
                "The plugin-specific installation settings."
            ),
            Operation(
                "feature",
                "feature",
                "Returns configured settings for the admitted host feature.",
                FeatureContexts(),
                [],
                PluginLuaValueShape.FeatureSettings,
                "The plugin-specific feature settings."
            )
        );

    public static PluginHostModuleDescriptor Diagnostics { get; } =
        Module(
            "diagnostics",
            Operation(
                "log",
                "log",
                "Writes a redaction-safe plugin diagnostic.",
                AllContexts(),
                [
                    Parameter(
                        "level",
                        PluginLuaValueShape.DiagnosticLevel,
                        "The diagnostic severity."
                    ),
                    Parameter("message", PluginLuaValueShape.String, "The safe diagnostic text."),
                ],
                PluginLuaValueShape.Nil,
                "No value."
            )
        );

    public static PluginHostModuleDescriptor Responses { get; } =
        Module(
            "responses",
            Operation(
                "chat",
                "chat",
                "Replies in the admitted channel.",
                Channel(),
                [Parameter("message", PluginLuaValueShape.String, "The response text.")],
                PluginLuaValueShape.Nil,
                "No value."
            ),
            Operation(
                "whisper",
                "whisper",
                "Replies privately to the admitted actor.",
                Channel(),
                [Parameter("message", PluginLuaValueShape.String, "The response text.")],
                PluginLuaValueShape.Nil,
                "No value."
            )
        );

    public static PluginHostModuleDescriptor Chat { get; } =
        Module(
            "chat",
            Operation(
                "send",
                "send",
                "Sends a message to the admitted channel.",
                Channel(),
                [Parameter("message", PluginLuaValueShape.String, "The chat message text.")],
                PluginLuaValueShape.Nil,
                "No value."
            )
        );

    public static PluginHostModuleDescriptor Overlay { get; } =
        Module(
            "overlay",
            Operation(
                "play-cue",
                "play_cue",
                "Admits an overlay cue for the current channel.",
                Channel(),
                [
                    Parameter(
                        "target_id",
                        PluginLuaValueShape.OverlayTargetId,
                        "The overlay target UUID."
                    ),
                    Parameter("cue_id", PluginLuaValueShape.OverlayCueId, "The overlay cue UUID."),
                ],
                PluginLuaValueShape.Nil,
                "No value."
            )
        );

    public static PluginHostModuleDescriptor Points { get; } =
        Module(
            "points",
            Operation(
                "add",
                "add",
                "Adds a non-negative point amount to a viewer balance.",
                Channel(),
                [
                    Parameter("viewer", PluginLuaValueShape.String, "The viewer login."),
                    Parameter(
                        "amount",
                        PluginLuaValueShape.PointAmount,
                        "The invariant non-negative amount."
                    ),
                    Parameter("reason", PluginLuaValueShape.String, "The audit reason."),
                ],
                PluginLuaValueShape.PointBalance,
                "The resulting invariant balance."
            )
        );

    public static PluginHostModuleDescriptor Twitch { get; } =
        Module(
            "twitch",
            Operation(
                "create-marker",
                "create_marker",
                "Creates a Twitch stream marker for the admitted channel.",
                Channel(),
                [Parameter("description", PluginLuaValueShape.String, "The marker description.")],
                PluginLuaValueShape.Nil,
                "No value."
            )
        );

    private static readonly Lazy<ImmutableArray<PluginHostModuleDescriptor>> _all = new(() =>
        [
            Context,
            Settings,
            Diagnostics,
            Responses,
            Chat,
            Overlay,
            Points,
            Twitch,
            Schedules!,
            Storage!,
            Http!,
        ]
    );

    public static ImmutableArray<PluginHostModuleDescriptor> All => _all.Value;

    private static PluginHostModuleDescriptor Module(
        string id,
        params PluginHostOperationDescriptor[] operations
    ) =>
        new(
            Identifier<PluginHostModuleId>(id, PluginHostModuleId.TryCreate),
            PluginApiVersion.V1,
            [.. operations]
        );

    private static PluginHostOperationDescriptor Operation(
        string id,
        string luaFunctionName,
        string description,
        ImmutableArray<PluginInvocationContextKind> contexts,
        ImmutableArray<PluginHostParameterDescriptor> parameters,
        PluginLuaValueShape resultShape,
        string resultDescription
    ) =>
        new(
            Identifier<PluginHostOperationId>(id, PluginHostOperationId.TryCreate),
            luaFunctionName,
            description,
            contexts,
            parameters,
            resultShape,
            resultDescription,
            PluginContractLimits.MaximumPluginValuePayloadBytes,
            PluginContractLimits.MaximumPluginValuePayloadBytes
        );

    private static PluginHostParameterDescriptor Parameter(
        string name,
        PluginLuaValueShape shape,
        string description
    ) => new(name, shape, description);

    private static ImmutableArray<PluginInvocationContextKind> Channel() =>
        [PluginInvocationContextKind.Channel];

    private static ImmutableArray<PluginInvocationContextKind> LiveContexts() =>
        [
            PluginInvocationContextKind.Installation,
            PluginInvocationContextKind.Channel,
            PluginInvocationContextKind.Automation,
            PluginInvocationContextKind.Page,
        ];

    private static ImmutableArray<PluginInvocationContextKind> FeatureContexts() =>
        [
            PluginInvocationContextKind.Channel,
            PluginInvocationContextKind.Automation,
            PluginInvocationContextKind.Page,
        ];

    private static ImmutableArray<PluginInvocationContextKind> AllContexts() =>
        [
            PluginInvocationContextKind.Installation,
            PluginInvocationContextKind.Channel,
            PluginInvocationContextKind.Automation,
            PluginInvocationContextKind.Migration,
            PluginInvocationContextKind.Page,
        ];

    private delegate bool TryIdentifier<TIdentifier>(string? value, out TIdentifier identifier);

    private static TIdentifier Identifier<TIdentifier>(
        string value,
        TryIdentifier<TIdentifier> create
    ) =>
        create(value, out var identifier)
            ? identifier
            : throw new InvalidOperationException("Invalid standard host identifier.");
}
