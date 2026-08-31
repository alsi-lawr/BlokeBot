using System.Text;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static class PluginProjectHandlerSkeletonEmitter
{
    internal static string Emit(PluginManifest manifest)
    {
        var catalog = PluginProjectHandlerCatalog.Create(manifest);
        var prefix = PluginProjectTypeEmitter.TypeName(manifest.Id.Value);
        var output = new StringBuilder();
        _ = output.AppendLine(
            "-- Generated from plugin.toml. Regenerate with blokebot-plugin generate; do not edit."
        );
        _ = output.AppendLine("local modules = {}");
        _ = output.AppendLine();
        for (var index = 0; index < manifest.LuaModules.Length; index++)
        {
            var module = manifest.LuaModules[index];
            var variable = $"module_{index + 1}";
            _ = output
                .Append("---@type ")
                .Append(prefix)
                .Append(PluginProjectTypeEmitter.TypeName(module.Id.Value))
                .AppendLine("Handlers");
            _ = output.Append("local ").Append(variable).AppendLine(" = {");
            foreach (
                var handlers in catalog
                    .Handlers.Where(handler => handler.Module == module.Id.Value)
                    .GroupBy(handler => handler.Operation, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
            )
            {
                var statements = handlers
                    .Select(static handler => handler.SkeletonStatement)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var statement = statements.Length == 1 ? statements[0] : "return input";
                _ = output.Append("  [\"").Append(handlers.Key).AppendLine("\"] = function(input)");
                _ = output.Append("    ").AppendLine(statement);
                _ = output.AppendLine("  end,");
            }
            _ = output.AppendLine("}");
            _ = output
                .Append("modules[\"")
                .Append(module.Id.Value)
                .Append("\"] = ")
                .AppendLine(variable);
            _ = output.AppendLine();
        }
        _ = output.AppendLine("return modules");
        return output.ToString();
    }
}
