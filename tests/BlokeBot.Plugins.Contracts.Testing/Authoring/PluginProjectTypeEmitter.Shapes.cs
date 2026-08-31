using System.Text;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static partial class PluginProjectTypeEmitter
{
    private static void AppendEventInput(
        string name,
        PluginInvocationInputSchemaDescriptor schema,
        StringBuilder output
    )
    {
        _ = output.Append("---@class ").Append(name).Append(": ").AppendLine(schema.LuaTypeName);
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
}
