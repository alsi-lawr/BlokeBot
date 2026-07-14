using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Replies;

namespace BlokeBot.Features.Guessing.Configuration;

public sealed class GuessingConfiguration
{
    public CommandAliasEditor Aliases { get; set; } = new();
    public ReplyDeliveryEditor ReplyDelivery { get; set; } = new();
    public bool WhisperResponsesEnabled { get; set; }
    public List<GuessRoundProfileSummary> Profiles { get; set; } = [];
    public GuessRoundProfileEditor Profile { get; set; } = new();
}
