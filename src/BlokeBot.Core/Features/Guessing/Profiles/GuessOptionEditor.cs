using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Guessing.Profiles;

public sealed class GuessOptionEditor
{
    public string Name { get; set; } = string.Empty;
    public string ReplyText { get; set; } = string.Empty;
    public ReplyDeliveryTarget ReplyTarget { get; set; } = ReplyDeliveryTarget.Chat;
}
