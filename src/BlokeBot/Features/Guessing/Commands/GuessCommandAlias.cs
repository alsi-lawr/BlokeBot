namespace BlokeBot.Features.Guessing.Commands;

public readonly record struct GuessCommandAlias
{
    public GuessCommandAlias(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsEmpty => Value.Length == 0;

    public static GuessCommandAlias Parse(string? value) =>
        new((value ?? string.Empty).Trim().TrimStart('!').ToLowerInvariant());

    public override string ToString() => Value;
}
