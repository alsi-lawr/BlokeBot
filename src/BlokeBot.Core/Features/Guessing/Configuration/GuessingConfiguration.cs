using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Replies;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public sealed class GuessingConfiguration
{
    internal GuessingConfiguration(
        CommandAliasEditor aliases,
        ReplyDeliveryEditor replyDelivery,
        bool whisperResponsesEnabled,
        IEnumerable<GuessRoundProfileSummary> profiles,
        GuessRoundProfileEditor profile
    )
    {
        Aliases = aliases;
        ReplyDelivery = replyDelivery;
        WhisperResponsesEnabled = whisperResponsesEnabled;
        Profiles = Array.AsReadOnly(profiles.ToArray());
        Profile = profile;
    }

    public CommandAliasEditor Aliases { get; set; } = new();
    public ReplyDeliveryEditor ReplyDelivery { get; set; } = new();
    public bool WhisperResponsesEnabled { get; set; }
    public IReadOnlyList<GuessRoundProfileSummary> Profiles { get; }
    public GuessRoundProfileEditor Profile { get; set; } = new();
}
