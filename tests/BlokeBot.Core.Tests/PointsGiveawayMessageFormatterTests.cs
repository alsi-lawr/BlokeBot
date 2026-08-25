using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointsGiveawayMessageFormatterTests
{
    private readonly PointsGiveawayMessageFormatter _formatter = new();
    private readonly PointsSettings _settings = new() { HostId = 7 };

    [Test]
    public void GiveawayReply_DeliveryMap_WhispersConfiguredFailureAndChatsDefault()
    {
        var delivery = ReplyDeliveryMap.FromWhisperKeys([PointsReplyKeys.GiveawayAlreadyActive]);

        _formatter
            .Reply(new PointsGiveawayStartOutcome.AlreadyActive(_settings), delivery)
            .Match(static success => success.Target, static failure => failure.Target)
            .ShouldBe(CommandResponseTarget.Whisper);
        _formatter
            .Reply(new PointsGiveawayStartOutcome.Started(_settings), delivery)
            .Match(static success => success.Target, static failure => failure.Target)
            .ShouldBe(CommandResponseTarget.Chat);
    }
}
