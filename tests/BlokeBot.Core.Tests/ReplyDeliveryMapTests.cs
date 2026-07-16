using BlokeBot.Commands;
using BlokeBot.Core.Features.Replies;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class ReplyDeliveryMapTests
{
    [Test]
    public void DeliveryEditor_SelectingWhisperThenChat_UsesNamedTargets()
    {
        var delivery = new ReplyDeliveryEditor();

        delivery.DeliverAsWhisper("winner");

        delivery.ToMap().TargetFor("winner").ShouldBe(CommandResponseTarget.Whisper);

        delivery.DeliverInChat("winner");

        delivery.ToMap().TargetFor("winner").ShouldBe(CommandResponseTarget.Chat);
    }

    [Test]
    public void DeliveryEditor_Snapshotting_CopiesWhisperSelections()
    {
        var editor = new ReplyDeliveryEditor();
        editor.DeliverAsWhisper("winner");

        var delivery = editor.ToMap();
        editor.DeliverInChat("winner");

        delivery.TargetFor("winner").ShouldBe(CommandResponseTarget.Whisper);
        editor.IsWhisper("winner").ShouldBeFalse();
    }

    [Test]
    public void DeliveryMap_Constructing_CopiesInput()
    {
        List<string> whisperKeys = ["winner"];

        var delivery = ReplyDeliveryMap.FromWhisperKeys(whisperKeys);
        whisperKeys.Clear();

        delivery.TargetFor("winner").ShouldBe(CommandResponseTarget.Whisper);
    }
}
