using BlokeBot.Features.Guessing.Guesses;

namespace BlokeBot.Features.Guessing.Profiles;

public readonly record struct GuessRoundProfileSlug
{
    public GuessRoundProfileSlug(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static GuessRoundProfileSlug FromName(string? value) =>
        new(GuessName.Parse(value).Value.Replace(' ', '-'));

    public override string ToString() => Value;
}
