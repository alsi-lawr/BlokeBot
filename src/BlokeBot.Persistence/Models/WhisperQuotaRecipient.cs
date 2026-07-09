namespace BlokeBot.Persistence.Models;

public sealed class WhisperQuotaRecipient
{
    public int Id { get; set; }

    public int WhisperQuotaBucketId { get; set; }

    public WhisperQuotaBucket WhisperQuotaBucket { get; set; } = null!;

    public string RecipientTwitchUserId { get; set; } = string.Empty;

    public string RecipientLogin { get; set; } = string.Empty;

    public DateTime FirstSentAtUtc { get; set; }
}
