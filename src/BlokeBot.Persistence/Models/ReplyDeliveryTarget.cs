using System.Diagnostics;

namespace BlokeBot.Persistence.Models;

public enum ReplyDeliveryTarget
{
    Chat,
    Whisper,
}

internal static class ReplyDeliveryTargetPersistence
{
    private const string _chatToken = "chat";
    private const string _whisperToken = "whisper";

    public static IReadOnlyList<string> Tokens { get; } = [_chatToken, _whisperToken];

    public static string ToToken(ReplyDeliveryTarget target) =>
        target switch
        {
            ReplyDeliveryTarget.Chat => _chatToken,
            ReplyDeliveryTarget.Whisper => _whisperToken,
            _ => throw new UnreachableException("Unknown reply delivery target."),
        };

    public static ReplyDeliveryTarget FromToken(string token) =>
        token switch
        {
            _chatToken => ReplyDeliveryTarget.Chat,
            _whisperToken => ReplyDeliveryTarget.Whisper,
            _ => throw new PersistenceDataIntegrityException(typeof(ReplyDeliveryTarget)),
        };
}
