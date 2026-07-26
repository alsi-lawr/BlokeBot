namespace BlokeBot.Persistence.Models;

public sealed class TwitchPollTemplate
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }

    public bool ChannelPointsVotingEnabled { get; set; }

    public int? ChannelPointsPerVote { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<TwitchPollTemplateChoice> Choices { get; set; } = [];
}
