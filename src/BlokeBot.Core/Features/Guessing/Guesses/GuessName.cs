namespace BlokeBot.Core.Features.Guessing.Guesses;

public readonly record struct GuessName
{
    public GuessName(string value) => Value = value;

    public string Value { get; }

    public bool IsEmpty => Value.Length == 0;

    public static GuessName Parse(string? value) =>
        new((value ?? string.Empty).Trim().ToLowerInvariant());

    public override string ToString() => Value;
}
