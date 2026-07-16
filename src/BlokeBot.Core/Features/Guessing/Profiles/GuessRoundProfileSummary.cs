namespace BlokeBot.Core.Features.Guessing.Profiles;

public sealed record GuessRoundProfileSummary(int Id, long Revision, string Name, bool IsDefault);
