namespace BlokeBot.Core.Features.Dashboards;

internal enum DashboardWidgetKind
{
    Automation,
    Value,
    Countdown,
    Poll,
    Prediction,
    Giveaway,
    Guessing,
}

internal static class DashboardWidgetKinds
{
    internal static DashboardWidgetKind Kind(DashboardWidgetConfiguration configuration) =>
        configuration.Match(
            _ => DashboardWidgetKind.Automation,
            _ => DashboardWidgetKind.Value,
            _ => DashboardWidgetKind.Countdown,
            _ => DashboardWidgetKind.Poll,
            _ => DashboardWidgetKind.Prediction,
            _ => DashboardWidgetKind.Giveaway,
            _ => DashboardWidgetKind.Guessing
        );

    internal static string Label(DashboardWidgetKind kind) =>
        kind switch
        {
            DashboardWidgetKind.Automation => "Automation button",
            DashboardWidgetKind.Value => "Value",
            DashboardWidgetKind.Countdown => "Countdown",
            DashboardWidgetKind.Poll => "Poll",
            DashboardWidgetKind.Prediction => "Prediction",
            DashboardWidgetKind.Giveaway => "Giveaway",
            DashboardWidgetKind.Guessing => "Guessing round",
        };

    internal static string Icon(DashboardWidgetKind kind) =>
        kind switch
        {
            DashboardWidgetKind.Automation => "play",
            DashboardWidgetKind.Value => "variables",
            DashboardWidgetKind.Countdown => "history",
            DashboardWidgetKind.Poll => "polls",
            DashboardWidgetKind.Prediction => "predictions",
            DashboardWidgetKind.Giveaway => "gift",
            DashboardWidgetKind.Guessing => "guessing",
        };

    internal static DashboardWidgetConfiguration Create(DashboardWidgetKind kind)
    {
        var binding = new DashboardBinding.Unconfigured(DashboardBindingIssue.Missing, null);
        return kind switch
        {
            DashboardWidgetKind.Automation => new DashboardWidgetConfiguration.Automation(
                binding,
                new DashboardTextOverride.Inherit()
            ),
            DashboardWidgetKind.Value => new DashboardWidgetConfiguration.Value(
                binding,
                new DashboardValueSelection.Scalar()
            ),
            DashboardWidgetKind.Countdown => new DashboardWidgetConfiguration.Countdown(
                binding,
                TimeSpan.FromMinutes(5)
            ),
            DashboardWidgetKind.Poll => new DashboardWidgetConfiguration.Poll(binding),
            DashboardWidgetKind.Prediction => new DashboardWidgetConfiguration.Prediction(binding),
            DashboardWidgetKind.Giveaway => new DashboardWidgetConfiguration.Giveaway(binding),
            DashboardWidgetKind.Guessing => new DashboardWidgetConfiguration.Guessing(binding),
        };
    }
}
