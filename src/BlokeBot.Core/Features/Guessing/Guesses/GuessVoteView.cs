namespace BlokeBot.Core.Features.Guessing.Guesses;

public sealed record GuessVoteView(string Login, string GuessName, DateTime GuessedAtUtc);
