using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Replies;

public sealed class ReplyDeliveryMap
{
    private readonly HashSet<string> whisperKeys;

    public ReplyDeliveryMap()
        : this([]) { }

    private ReplyDeliveryMap(IEnumerable<string> whisperKeys)
    {
        this.whisperKeys = whisperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> WhisperKeys => whisperKeys;

    public static ReplyDeliveryMap FromSettings(IEnumerable<ReplyDeliverySetting> settings) =>
        new(
            settings
                .Where(x =>
                    string.Equals(
                        x.Target,
                        ReplyDeliveryTargets.Whisper,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Select(x => x.ReplyKey)
        );

    public bool IsWhisper(string replyKey) => whisperKeys.Contains(replyKey);

    public ReplyDeliveryMap Only(IEnumerable<string> allowedKeys)
    {
        var allowed = allowedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ReplyDeliveryMap(whisperKeys.Where(allowed.Contains));
    }

    public TwitchCommandResponseTarget TargetFor(string replyKey) =>
        IsWhisper(replyKey)
            ? TwitchCommandResponseTarget.Whisper
            : TwitchCommandResponseTarget.Chat;

    public void SetWhisper(string replyKey, bool whisper)
    {
        if (whisper)
            whisperKeys.Add(replyKey);
        else
            whisperKeys.Remove(replyKey);
    }
}
