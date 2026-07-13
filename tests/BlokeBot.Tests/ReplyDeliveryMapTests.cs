using BlokeBot.Commands;
using BlokeBot.Features.Replies;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class ReplyDeliveryMapTests
{
    [Test]
    public void DeliveryCommands_SelectingWhisperThenChat_UseNamedTargets()
    {
        var delivery = new ReplyDeliveryMap();

        delivery.DeliverAsWhisper("winner");

        delivery.TargetFor("winner").ShouldBe(CommandResponseTarget.Whisper);

        delivery.DeliverInChat("winner");

        delivery.TargetFor("winner").ShouldBe(CommandResponseTarget.Chat);
    }
}
