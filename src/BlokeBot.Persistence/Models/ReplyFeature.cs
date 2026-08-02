using System.Diagnostics;

namespace BlokeBot.Persistence.Models;

public enum ReplyFeature
{
    Guessing,
    Points,
}

internal static class ReplyFeaturePersistence
{
    private const string _guessingToken = "guessing";
    private const string _pointsToken = "points";

    public static IReadOnlyList<string> Tokens { get; } = [_guessingToken, _pointsToken];

    public static string ToToken(ReplyFeature feature) =>
        feature switch
        {
            ReplyFeature.Guessing => _guessingToken,
            ReplyFeature.Points => _pointsToken,
            _ => throw new UnreachableException("Unknown reply feature."),
        };

    public static ReplyFeature FromToken(string token) =>
        token switch
        {
            _guessingToken => ReplyFeature.Guessing,
            _pointsToken => ReplyFeature.Points,
            _ => throw new PersistenceDataIntegrityException(typeof(ReplyFeature)),
        };
}
