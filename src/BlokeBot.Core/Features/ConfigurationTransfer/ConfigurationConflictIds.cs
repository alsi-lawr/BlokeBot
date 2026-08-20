namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class ConfigurationConflictIds
{
    public static string CustomCommandAlias(string commandId, string alias) =>
        $"alias:{commandId}:{alias}";
}
