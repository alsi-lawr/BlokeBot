namespace BlokeBot.Persistence.Models;

public sealed class TwitchPoll
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string ProviderPollId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ChoicesJson { get; set; } = "[]";

    public TwitchPollStatus Status { get; set; }

    public bool IsExternallyStarted { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
