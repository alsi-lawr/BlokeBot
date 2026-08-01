namespace BlokeBot.Core.Features.Commands;

internal static class CommandAliasScopePersistence
{
    public static int? ToProfileId(CommandAliasScope scope) =>
        scope.Match<int?>(static _ => null, static profile => profile.ProfileId);

    public static CommandAliasScope FromProfileId(int? profileId) =>
        profileId is { } value
            ? new CommandAliasScope.Profile(value)
            : new CommandAliasScope.Global();
}
