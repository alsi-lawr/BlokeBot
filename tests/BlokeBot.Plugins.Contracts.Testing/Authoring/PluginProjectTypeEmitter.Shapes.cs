using System.Text;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static partial class PluginProjectTypeEmitter
{
    private static void AppendDerivedInput(
        PluginProjectDerivedInputDescriptor input,
        StringBuilder output
    )
    {
        _ = output.Append("---").AppendLine(input.Schema.Description);
        _ = output.Append("---@class ").Append(input.TypeName);
        if (input.ExtendsSchema)
        {
            _ = output.Append(": ").Append(input.Schema.LuaTypeName);
        }
        _ = output.AppendLine();
        if (!input.ExtendsSchema)
        {
            foreach (var field in input.Schema.Fields)
            {
                _ = output
                    .Append("---@field [\"")
                    .Append(field.Name)
                    .Append("\"]")
                    .Append(field.Required ? string.Empty : "?")
                    .Append(' ')
                    .Append(field.Shape.LuaTypeName)
                    .Append(" # ")
                    .AppendLine(field.Description);
            }
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
}
