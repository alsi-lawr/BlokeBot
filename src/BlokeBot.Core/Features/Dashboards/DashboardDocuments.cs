using System.Text.Json;

namespace BlokeBot.Core.Features.Dashboards;

internal static class DashboardDocuments
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static string Serialize(DashboardDocument document) =>
        JsonSerializer.Serialize(document, _json);

    internal static DashboardDocument Deserialize(string json)
    {
        var document = (
            JsonSerializer.Deserialize<DashboardDocument>(json, _json)
            ?? throw new InvalidOperationException("The stored dashboard document is missing.")
        ).InitializePhone();
        return Valid(document)
            ? document
            : throw new JsonException("The stored dashboard arrangements are invalid.");
    }

    internal static bool Valid(DashboardDocument document) =>
        !document.Widgets.IsDefault
        && document.Widgets.Select(widget => widget.Id).Distinct().Count()
            == document.Widgets.Length
        && document.Widgets.All(widget =>
            widget.Id.Value != Guid.Empty && widget.Label is not null && Valid(widget.Configuration)
        )
        && document.Desktop is not null
        && document.Phone is not null
        && DashboardArrangements.Valid(
            document.Desktop,
            document.Widgets.Select(w => w.Id).ToArray()
        )
        && DashboardArrangements.Valid(
            document.Phone,
            document.Widgets.Select(w => w.Id).ToArray()
        );

    private static bool Valid(DashboardWidgetConfiguration configuration) =>
        configuration.Match(
            automation =>
                ValidBinding<DashboardTarget.Automation>(automation.Binding)
                && automation.Data.Match(
                    _ => true,
                    text => text.Text is not null && text.Text.Length <= 500
                ),
            value =>
                ValidBinding<DashboardTarget.Value>(value.Binding)
                && value.Selection.Match(_ => true, entry => !string.IsNullOrWhiteSpace(entry.Key)),
            countdown =>
                ValidBinding<DashboardTarget.Countdown>(countdown.Binding)
                && countdown.DefaultDuration.TotalSeconds is >= 1 and <= 31536000
                && countdown.DefaultDuration.Ticks % TimeSpan.TicksPerSecond == 0,
            poll => ValidBinding<DashboardTarget.Poll>(poll.Binding),
            prediction => ValidBinding<DashboardTarget.Prediction>(prediction.Binding),
            giveaway => ValidBinding<DashboardTarget.Giveaway>(giveaway.Binding),
            guessing => ValidBinding<DashboardTarget.Guessing>(guessing.Binding)
        );

    private static bool ValidBinding<TTarget>(DashboardBinding binding)
        where TTarget : DashboardTarget =>
        binding.Match(
            configured =>
                configured.Target is TTarget
                && configured.Target.Match(
                    automation =>
                        automation.FlowId.Value != Guid.Empty
                        && automation.SourceNodeId.Value != Guid.Empty,
                    value => value.DefinitionId > 0,
                    countdown =>
                        !string.IsNullOrWhiteSpace(countdown.Name) && countdown.Name.Length <= 96,
                    poll => poll.TemplateId > 0,
                    prediction => prediction.TemplateId > 0,
                    _ => true,
                    guessing => guessing.ProfileId > 0
                ),
            _ => true
        );
}
