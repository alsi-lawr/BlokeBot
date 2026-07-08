using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Profiles;

namespace BlokeBot.Features.Guessing.Configuration;

public sealed class GuessingConfiguration
{
    public CommandAliasEditor Aliases { get; set; } = new();
    public List<GuessRoundProfileSummary> Profiles { get; set; } = [];
    public GuessRoundProfileEditor Profile { get; set; } = new();
}
