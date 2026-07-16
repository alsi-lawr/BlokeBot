namespace BlokeBot.Core.Features.Commands;

internal static class CommandAliasScopePersistence
{
    public static int? ToProfileId(CommandAliasScope scope)
    {
        return scope.Match<int?>(static _ => null, static profile => profile.ProfileId);
    }

    public static CommandAliasScope FromProfileId(int? profileId)
    {
        return profileId is { } value
            ? new CommandAliasScope.Profile(value)
            : new CommandAliasScope.Global();
    }
}
