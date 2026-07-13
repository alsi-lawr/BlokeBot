using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Guessing.Commands;

public static class GuessingAppCommandKindMap
{
    public static IReadOnlySet<AppCommandKind> AppKinds { get; } =
        new HashSet<AppCommandKind>
        {
            AppCommandKind.Start,
            AppCommandKind.Stop,
            AppCommandKind.Win,
            AppCommandKind.Guess,
            AppCommandKind.Guesses,
        };

    public static AppCommandKind ToAppKind(GuessCommandKind kind)
    {
        return kind switch
        {
            GuessCommandKind.Start => AppCommandKind.Start,
            GuessCommandKind.Stop => AppCommandKind.Stop,
            GuessCommandKind.Win => AppCommandKind.Win,
            GuessCommandKind.Guess => AppCommandKind.Guess,
            GuessCommandKind.Guesses => AppCommandKind.Guesses,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public static bool TryFromAppKind(AppCommandKind appKind, out GuessCommandKind kind)
    {
        kind = appKind switch
        {
            AppCommandKind.Start => GuessCommandKind.Start,
            AppCommandKind.Stop => GuessCommandKind.Stop,
            AppCommandKind.Win => GuessCommandKind.Win,
            AppCommandKind.Guess => GuessCommandKind.Guess,
            AppCommandKind.Guesses => GuessCommandKind.Guesses,
            _ => default,
        };
        return AppKinds.Contains(appKind);
    }
}
