using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Replies;

public sealed class ReplyDeliveryMap
{
    private readonly HashSet<string> _whisperKeys;

    public ReplyDeliveryMap()
        : this([]) { }

    private ReplyDeliveryMap(IEnumerable<string> whisperKeys)
    {
        _whisperKeys = whisperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> WhisperKeys => _whisperKeys;

    public static ReplyDeliveryMap FromSettings(IEnumerable<ReplyDeliverySetting> settings)
    {
        return new(settings.Where(x => x.Target.IsWhisper()).Select(x => x.ReplyKey));
    }

    public bool IsWhisper(string replyKey)
    {
        return _whisperKeys.Contains(replyKey);
    }

    public ReplyDeliveryMap Only(IEnumerable<string> allowedKeys)
    {
        var allowed = allowedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ReplyDeliveryMap(_whisperKeys.Where(allowed.Contains));
    }

    public CommandResponseTarget TargetFor(string replyKey)
    {
        return IsWhisper(replyKey) ? CommandResponseTarget.Whisper : CommandResponseTarget.Chat;
    }

    public void DeliverAsWhisper(string replyKey)
    {
        _whisperKeys.Add(replyKey);
    }

    public void DeliverInChat(string replyKey)
    {
        _whisperKeys.Remove(replyKey);
    }
}
