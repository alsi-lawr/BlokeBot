using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferDashboardTests
{
    [Test]
    [Arguments("missing")]
    [Arguments("duplicate")]
    [Arguments("unknown")]
    [Arguments("overlap")]
    [Arguments("bounds")]
    [Arguments("overflow")]
    public void CodecRejectsInvalidPhoneWithoutRegeneratingIt(string invalid)
    {
        var layout = Layout(
            new DashboardConfigurationV1.Giveaway(new DashboardReferenceV1.Giveaway()),
            new DashboardConfigurationV1.Giveaway(new DashboardReferenceV1.Giveaway())
        );
        var first = new DashboardWidgetPlacementV1(layout.Widgets[0].Id, new(0, 0, 4, 3, 0));
        var second = new DashboardWidgetPlacementV1(layout.Widgets[1].Id, new(0, 3, 4, 3, 1));
        DashboardWidgetPlacementV1[] placements = invalid switch
        {
            "missing" => [first],
            "duplicate" => [first, second with { WidgetId = first.WidgetId }],
            "unknown" => [first, second with { WidgetId = "not-exported" }],
            "overlap" => [first, second with { Placement = second.Placement with { Row = 2 } }],
            "bounds" => [first, second with { Placement = second.Placement with { Column = 1 } }],
            "overflow" =>
            [
                first,
                second with
                {
                    Placement = second.Placement with { Row = int.MaxValue },
                },
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        var codec = new ConfigurationDocumentCodec();
        var json = codec.Serialize(Document(new([layout with { Phone = new(4, 72, placements) }])));

        _ = codec.Parse(json).ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
    }
}
