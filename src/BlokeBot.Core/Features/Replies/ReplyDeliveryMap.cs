using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Replies;

public sealed class ReplyDeliveryMap
{
    private readonly HashSet<string> _whisperKeys;

    public ReplyDeliveryMap()
        : this([]) { }

    private ReplyDeliveryMap(IEnumerable<string> whisperKeys)
    {
        _whisperKeys = whisperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        WhisperKeys = Array.AsReadOnly(
            _whisperKeys.Order(StringComparer.OrdinalIgnoreCase).ToArray()
        );
    }

    public IReadOnlyCollection<string> WhisperKeys { get; }

    public static ReplyDeliveryMap FromWhisperKeys(IEnumerable<string> whisperKeys) =>
        new(whisperKeys);

    public static ReplyDeliveryMap FromSettings(IEnumerable<ReplyDeliverySetting> settings) =>
        new(settings.Where(static x => x.Target.IsWhisper()).Select(static x => x.ReplyKey));

    public bool IsWhisper(string replyKey) => _whisperKeys.Contains(replyKey);

    public ReplyDeliveryMap Only(IEnumerable<string> allowedKeys)
    {
        var allowed = allowedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ReplyDeliveryMap(_whisperKeys.Where(allowed.Contains));
    }

    public CommandResponseTarget TargetFor(string replyKey) =>
        IsWhisper(replyKey) ? CommandResponseTarget.Whisper : CommandResponseTarget.Chat;
}
