namespace BlokeBot.Core.Features.Replies;

public sealed class ReplyDeliveryEditor
{
    private readonly HashSet<string> _whisperKeys;

    public ReplyDeliveryEditor()
        : this([]) { }

    private ReplyDeliveryEditor(IEnumerable<string> whisperKeys) =>
        _whisperKeys = whisperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static ReplyDeliveryEditor From(ReplyDeliveryMap delivery) => new(delivery.WhisperKeys);

    public bool IsWhisper(string replyKey) => _whisperKeys.Contains(replyKey);

    public void DeliverAsWhisper(string replyKey) => _whisperKeys.Add(replyKey);

    public void DeliverInChat(string replyKey) => _whisperKeys.Remove(replyKey);

    public ReplyDeliveryMap ToMap() => ReplyDeliveryMap.FromWhisperKeys(_whisperKeys);
}
