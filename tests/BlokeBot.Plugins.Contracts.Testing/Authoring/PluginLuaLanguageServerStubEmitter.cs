using System.Globalization;
using System.Text;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static class PluginLuaLanguageServerStubEmitter
{
    internal static string Emit(PluginAuthoringContract contract)
    {
        var output = new StringBuilder();
        _ = output.AppendLine("---@meta");
        _ = output.AppendLine();
        _ = output
            .Append("-- Generated from BlokeBot host API v")
            .Append(contract.Runtime.HostApiVersion.Value)
            .AppendLine(" for Lua 5.4.");
        _ = output.AppendLine("-- Regenerate the author artifacts instead of editing this file.");
        _ = output.AppendLine();
        _ = output.AppendLine(
            "---@alias BlokeBotValue nil|boolean|number|string|BlokeBotValue[]|table<string, BlokeBotValue>"
        );
        _ = output.AppendLine();
        _ = output.AppendLine("---@class BlokeBotHost");
        _ = output.AppendLine("local host = {}");
        _ = output.AppendLine();

        foreach (
            var operation in contract.HostModules.SelectMany(module =>
                module.Operations.Select(candidate => (Module: module, Operation: candidate))
            )
        )
        {
            _ = output
                .Append("---@overload fun(module: ")
                .Append(Quoted(operation.Module.Id.Value))
                .Append(", operation: ")
                .Append(Quoted(operation.Operation.Id.Value));
            for (var index = 0; index < operation.Operation.ArgumentKinds.Length; index++)
            {
                _ = output
                    .Append(", argument")
                    .Append((index + 1).ToString(CultureInfo.InvariantCulture))
                    .Append(": ")
                    .Append(LuaType(operation.Operation.ArgumentKinds[index]));
            }

            _ = output.Append("): ").AppendLine(LuaType(operation.Operation.ResultKind));
        }

        _ = output.AppendLine("---@param module string");
        _ = output.AppendLine("---@param operation string");
        _ = output.AppendLine("---@param ... BlokeBotValue");
        _ = output.AppendLine("---@return BlokeBotValue");
        _ = output.AppendLine("function host.call(module, operation, ...) end");
        _ = output.AppendLine();
        _ = output.AppendLine("---@class BlokeBot");
        _ = output.AppendLine("---@field host BlokeBotHost");
        _ = output.AppendLine("blokebot = { host = host }");
        return output.ToString();
    }

    private static string LuaType(PluginValueKind kind) =>
        kind switch
        {
            PluginValueKind.Nil => "nil",
            PluginValueKind.Boolean => "boolean",
            PluginValueKind.Number => "number",
            PluginValueKind.String => "string",
            PluginValueKind.Array => "BlokeBotValue[]",
            PluginValueKind.Map => "table<string, BlokeBotValue>",
        };

    private static string Quoted(string value) => $"\"{value}\"";
}
