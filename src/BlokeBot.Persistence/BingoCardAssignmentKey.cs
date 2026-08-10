namespace BlokeBot.Persistence;

internal static class BingoCardAssignmentKey
{
    internal const string LegacyUniquePrefix = "viewer:";

    internal static string Opaque(Guid cardPublicId) => $"card:{cardPublicId:N}";
}
