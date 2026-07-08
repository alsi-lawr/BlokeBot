namespace BlokeBot.Persistence.Models;

public enum AppCommandKind
{
    Start,
    Stop,
    Win,
    Guess,
    Guesses,
    Points,
    GivePoints,
    AddPoints,
    RemovePoints,
    Gamble,
    Giveaway,
    Join,
    EndGiveaway,
    CancelGiveaway,
}

public static class AppCommandKindStore
{
    public static IReadOnlyList<string> Values { get; } =
    [
        Format(AppCommandKind.AddPoints),
        Format(AppCommandKind.CancelGiveaway),
        Format(AppCommandKind.EndGiveaway),
        Format(AppCommandKind.Gamble),
        Format(AppCommandKind.Giveaway),
        Format(AppCommandKind.GivePoints),
        Format(AppCommandKind.Guess),
        Format(AppCommandKind.Guesses),
        Format(AppCommandKind.Join),
        Format(AppCommandKind.Points),
        Format(AppCommandKind.RemovePoints),
        Format(AppCommandKind.Start),
        Format(AppCommandKind.Stop),
        Format(AppCommandKind.Win),
    ];

    public static string Format(AppCommandKind kind) => kind.ToString();

    public static AppCommandKind Parse(string value) =>
        Enum.TryParse<AppCommandKind>(value, ignoreCase: true, out var kind)
            ? kind
            : throw new FormatException($"Unknown app command kind '{value}'.");
}
