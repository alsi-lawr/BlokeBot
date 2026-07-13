using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Replies;

internal static class ReplyDeliveryTargetExtensions
{
    public static bool IsWhisper(this ReplyDeliveryTarget target)
    {
        return target switch
        {
            ReplyDeliveryTarget.Chat => false,
            ReplyDeliveryTarget.Whisper => true,
            _ => throw new UnreachableException("Unknown reply delivery target."),
        };
    }
}
