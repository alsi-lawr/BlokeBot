using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public static class PluginStandardHostModules
{
    public static PluginHostModuleDescriptor Context { get; } =
        Module("context", Operation("current", AllContexts(), [], PluginValueKind.Map));

    public static PluginHostModuleDescriptor Settings { get; } =
        Module(
            "settings",
            Operation("installation", LiveContexts(), [], PluginValueKind.Map),
            Operation("feature", FeatureContexts(), [], PluginValueKind.Map)
        );

    public static PluginHostModuleDescriptor Diagnostics { get; } =
        Module(
            "diagnostics",
            Operation(
                "log",
                AllContexts(),
                [PluginValueKind.String, PluginValueKind.String],
                PluginValueKind.Nil
            )
        );

    public static PluginHostModuleDescriptor Responses { get; } =
        Module(
            "responses",
            Operation("chat", Channel(), [PluginValueKind.String], PluginValueKind.Nil),
            Operation("whisper", Channel(), [PluginValueKind.String], PluginValueKind.Nil)
        );

    public static PluginHostModuleDescriptor Chat { get; } =
        Module("chat", Operation("send", Channel(), [PluginValueKind.String], PluginValueKind.Nil));

    public static PluginHostModuleDescriptor Overlay { get; } =
        Module(
            "overlay",
            Operation(
                "play-cue",
                Channel(),
                [PluginValueKind.String, PluginValueKind.String],
                PluginValueKind.Nil
            )
        );

    public static PluginHostModuleDescriptor Points { get; } =
        Module(
            "points",
            Operation(
                "add",
                Channel(),
                [PluginValueKind.String, PluginValueKind.String, PluginValueKind.String],
                PluginValueKind.String
            )
        );

    public static PluginHostModuleDescriptor Twitch { get; } =
        Module(
            "twitch",
            Operation("create-marker", Channel(), [PluginValueKind.String], PluginValueKind.Nil)
        );

    public static PluginHostModuleDescriptor Schedules { get; } =
        Module(
            "schedules",
            Operation(
                "once",
                Channel(),
                [PluginValueKind.String, PluginValueKind.String, PluginValueKind.Map],
                PluginValueKind.String
            ),
            Operation(
                "recurring",
                Channel(),
                [
                    PluginValueKind.String,
                    PluginValueKind.String,
                    PluginValueKind.Number,
                    PluginValueKind.Map,
                ],
                PluginValueKind.String
            ),
            Operation("cancel", Channel(), [PluginValueKind.String], PluginValueKind.Nil)
        );

    public static PluginHostModuleDescriptor Storage { get; } =
        Module(
            "storage",
            Operation(
                "execute",
                StorageContexts(),
                [PluginValueKind.String, PluginValueKind.Map],
                PluginValueKind.Number
            ),
            Operation(
                "query",
                StorageContexts(),
                [PluginValueKind.String, PluginValueKind.Map],
                PluginValueKind.Array
            )
        );

    public static PluginHostModuleDescriptor Http { get; } =
        Module(
            "http",
            Operation("send", LiveContexts(), [PluginValueKind.Map], PluginValueKind.Map)
        );

    public static ImmutableArray<PluginHostModuleDescriptor> All { get; } =
    [
        Context,
        Settings,
        Diagnostics,
        Responses,
        Chat,
        Overlay,
        Points,
        Twitch,
        Schedules,
        Storage,
        Http,
    ];

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
        ImmutableArray<PluginInvocationContextKind> contexts,
        ImmutableArray<PluginValueKind> arguments,
        PluginValueKind result
    ) =>
        new(
            Identifier<PluginHostOperationId>(id, PluginHostOperationId.TryCreate),
            contexts,
            arguments,
            result,
            PluginContractLimits.MaximumPluginValuePayloadBytes,
            PluginContractLimits.MaximumPluginValuePayloadBytes
        );

    private static ImmutableArray<PluginInvocationContextKind> Channel() =>
        [PluginInvocationContextKind.Channel];

    private static ImmutableArray<PluginInvocationContextKind> StorageContexts() =>
        [
            PluginInvocationContextKind.Installation,
            PluginInvocationContextKind.Channel,
            PluginInvocationContextKind.Automation,
            PluginInvocationContextKind.Migration,
            PluginInvocationContextKind.Page,
        ];

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
