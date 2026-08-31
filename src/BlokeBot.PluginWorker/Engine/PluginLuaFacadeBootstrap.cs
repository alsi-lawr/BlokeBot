using System.Text;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.PluginWorker;

internal static class PluginLuaFacadeBootstrap
{
    internal static string Emit(string hostCallMarker)
    {
        var output = new StringBuilder();
        _ = output.AppendLine("local function dispatch(module, operation, ...)");
        _ = output
            .Append("  local response = coroutine.yield({ marker = ")
            .Append(Quoted(hostCallMarker))
            .AppendLine(", module = module, operation = operation, arguments = {...} })");
        _ = output.AppendLine("  if response.kind == 'returned' then return response.value end");
        _ = output.AppendLine("  error(response, 0)");
        _ = output.AppendLine("end");
        _ = output.AppendLine();
        _ = output
            .Append("local blokebot = { api_version = ")
            .Append(PluginRuntimeContract.Current.HostApiVersion.Value)
            .AppendLine(" }");
        foreach (var module in PluginStandardHostModules.All)
        {
            _ = output.Append("blokebot.").Append(module.Id.Value).AppendLine(" = {}");
            foreach (var operation in module.Operations)
            {
                _ = output
                    .Append("function blokebot.")
                    .Append(module.Id.Value)
                    .Append('.')
                    .Append(operation.LuaFunctionName)
                    .Append('(')
                    .AppendJoin(", ", operation.Parameters.Select(parameter => parameter.Name))
                    .AppendLine(")");
                _ = output
                    .Append("  return dispatch(")
                    .Append(Quoted(module.Id.Value))
                    .Append(", ")
                    .Append(Quoted(operation.Id.Value));
                foreach (var parameter in operation.Parameters)
                {
                    _ = output.Append(", ").Append(parameter.Name);
                }
                _ = output.AppendLine(")");
                _ = output.AppendLine("end");
            }
        }
        _ = output.AppendLine("package.preload['blokebot'] = function() return blokebot end");
        return output.ToString();
    }

    private static string Quoted(string value) =>
        $"'{value.Replace("'", "\\'", StringComparison.Ordinal)}'";
}
