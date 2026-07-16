using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Guessing.Commands;

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

    public static Option<GuessCommandKind> FromAppKind(AppCommandKind appKind)
    {
        return appKind switch
        {
            AppCommandKind.Start => Option<GuessCommandKind>.Some(GuessCommandKind.Start),
            AppCommandKind.Stop => Option<GuessCommandKind>.Some(GuessCommandKind.Stop),
            AppCommandKind.Win => Option<GuessCommandKind>.Some(GuessCommandKind.Win),
            AppCommandKind.Guess => Option<GuessCommandKind>.Some(GuessCommandKind.Guess),
            AppCommandKind.Guesses => Option<GuessCommandKind>.Some(GuessCommandKind.Guesses),
            _ => Option<GuessCommandKind>.None,
        };
    }
}
