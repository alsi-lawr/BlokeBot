using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public static partial class PluginStandardHostModules
{
    public static PluginHostModuleDescriptor Schedules { get; } =
        Module(
            "schedules",
            Operation(
                "once",
                "once",
                "Creates a one-time feature schedule.",
                Channel(),
                [
                    Parameter("handler_id", PluginLuaValueShape.String, "The declared handler ID."),
                    Parameter(
                        "due_at",
                        PluginLuaValueShape.String,
                        "The future UTC timestamp in ISO 8601 format."
                    ),
                    Parameter("input", PluginLuaValueShape.ScheduleInput, "The handler input."),
                ],
                PluginLuaValueShape.ScheduleId,
                "The new schedule UUID."
            ),
            Operation(
                "recurring",
                "recurring",
                "Creates a recurring feature schedule.",
                Channel(),
                [
                    Parameter("handler_id", PluginLuaValueShape.String, "The declared handler ID."),
                    Parameter(
                        "due_at",
                        PluginLuaValueShape.String,
                        "The first future UTC timestamp in ISO 8601 format."
                    ),
                    Parameter(
                        "interval_seconds",
                        PluginLuaValueShape.ScheduleIntervalSeconds,
                        "The positive whole-second interval."
                    ),
                    Parameter("input", PluginLuaValueShape.ScheduleInput, "The handler input."),
                ],
                PluginLuaValueShape.ScheduleId,
                "The new schedule UUID."
            ),
            Operation(
                "cancel",
                "cancel",
                "Cancels a schedule owned by the current feature generation.",
                Channel(),
                [Parameter("schedule_id", PluginLuaValueShape.ScheduleId, "The schedule UUID.")],
                PluginLuaValueShape.Nil,
                "No value."
            )
        );

    public static PluginHostModuleDescriptor Storage { get; } =
        Module(
            "storage",
            Operation(
                "execute",
                "execute",
                "Executes one supported statement against plugin-private SQLite.",
                StorageContexts(),
                [
                    Parameter("sql", PluginLuaValueShape.String, "The SQL statement."),
                    Parameter(
                        "parameters",
                        PluginLuaValueShape.SqlParameters,
                        "Named scalar parameters without the SQL prefix."
                    ),
                ],
                PluginLuaValueShape.Number,
                "The number of changed rows."
            ),
            Operation(
                "query",
                "query",
                "Queries plugin-private SQLite.",
                StorageContexts(),
                [
                    Parameter("sql", PluginLuaValueShape.String, "The SQL query."),
                    Parameter(
                        "parameters",
                        PluginLuaValueShape.SqlParameters,
                        "Named scalar parameters without the SQL prefix."
                    ),
                ],
                PluginLuaValueShape.SqlRows,
                "The result rows keyed by column name."
            )
        );

    public static PluginHostModuleDescriptor Http { get; } =
        Module(
            "http",
            Operation(
                "send",
                "send",
                "Sends a bounded outbound HTTP request through the host policy.",
                LiveContexts(),
                [
                    Parameter(
                        "request",
                        PluginLuaValueShape.HttpRequest,
                        "The method, absolute URL, optional headers, and optional UTF-8 body."
                    ),
                ],
                PluginLuaValueShape.HttpOutcome,
                "A tagged response, rejection, or transport failure."
            )
        );

    private static ImmutableArray<PluginInvocationContextKind> StorageContexts() =>
        [
            PluginInvocationContextKind.Installation,
            PluginInvocationContextKind.Channel,
            PluginInvocationContextKind.Automation,
            PluginInvocationContextKind.Migration,
            PluginInvocationContextKind.Page,
        ];
}
