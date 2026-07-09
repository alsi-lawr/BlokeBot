namespace BlokeBot.Persistence.Models;

public sealed class WhisperQuotaBucket
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string BotTwitchUserId { get; set; } = string.Empty;

    public DateTime DayUtc { get; set; }

    public bool Exhausted { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<WhisperQuotaRecipient> Recipients { get; set; } = [];
}
