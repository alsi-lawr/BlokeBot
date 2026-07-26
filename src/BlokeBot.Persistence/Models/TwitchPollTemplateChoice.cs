namespace BlokeBot.Persistence.Models;

public sealed class TwitchPollTemplateChoice
{
    public int Id { get; set; }

    public int TwitchPollTemplateId { get; set; }

    public TwitchPollTemplate Template { get; set; } = null!;

    public int Position { get; set; }

    public string Title { get; set; } = string.Empty;
}
