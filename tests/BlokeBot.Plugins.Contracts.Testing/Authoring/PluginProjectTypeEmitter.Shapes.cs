using System.Text;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static partial class PluginProjectTypeEmitter
{
    private static void AppendEventInput(
        string name,
        PluginEventSource source,
        StringBuilder output
    )
    {
        _ = output.Append("---@class ").Append(name).AppendLine(": table<string, BlokeBotValue>");
        if (source is not PluginEventSource.TwitchRaw)
        {
            _ = output.AppendLine("---@field event_id string");
            _ = output.AppendLine("---@field source string");
        }
        if (source is PluginEventSource.Twitch)
        {
            _ = output.AppendLine("---@field occurred_at string");
        }
        _ = output.AppendLine();
    }

    private static string ValueType(PluginValueKind kind) =>
        kind switch
        {
            PluginValueKind.Nil => "nil",
            PluginValueKind.Boolean => "boolean",
            PluginValueKind.Number => "number",
            PluginValueKind.String => "string",
            PluginValueKind.Array => "BlokeBotValue[]",
            PluginValueKind.Map => "table<string, BlokeBotValue>",
        };

    private sealed record Handler(string Module, string Operation, string Input, string Result);
}
