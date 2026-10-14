using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Dashboards.Page;

public partial class DashboardWidgetSettings
{
    [Parameter, EditorRequired]
    public required DashboardWidgetView View { get; set; }

    [Parameter]
    public required DashboardPlacementTransport Placement { get; set; }

    [Parameter, EditorRequired]
    public required DashboardEditorTargets Targets { get; set; }

    [Parameter]
    public EventCallback<DashboardWidgetView> Changed { get; set; }

    [Parameter]
    public EventCallback<DashboardPlacementTransport> PlacementChanged { get; set; }
    private DashboardWidget _widget => View.Widget;
    private IReadOnlyList<DashboardTargetOption> _options =>
        Targets.Options(DashboardWidgetKinds.Kind(_widget.Configuration));
    private string _selectedKey =>
        _widget.Configuration.Binding is DashboardBinding.Configured bound
            ? _options.FirstOrDefault(o => o.Target == bound.Target)?.Key ?? string.Empty
            : string.Empty;

    private Task ChangeAsync(DashboardWidget widget) =>
        Changed.InvokeAsync(new DashboardWidgetView(widget));

    private Task ConfigurationAsync(DashboardWidgetConfiguration configuration) =>
        ChangeAsync(_widget with { Configuration = configuration });

    private Task TargetChangedAsync(ChangeEventArgs args)
    {
        var target = _options.FirstOrDefault(o => o.Key == args.Value?.ToString());
        DashboardBinding binding = target is null
            ? new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected)
            : new DashboardBinding.Configured(target.Target);
        return ConfigurationAsync(_widget.Configuration with { Binding = binding });
    }

    private Task TimerNameAsync(string name) =>
        ConfigurationAsync(
            _widget.Configuration with
            {
                Binding = string.IsNullOrWhiteSpace(name)
                    ? new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected)
                    : new DashboardBinding.Configured(new DashboardTarget.Countdown(name.Trim())),
            }
        );

    private Task DurationAsync(ChangeEventArgs args) =>
        int.TryParse(args.Value?.ToString(), out var seconds)
        && seconds > 0
        && seconds <= 365 * 24 * 3600
        && _widget.Configuration is DashboardWidgetConfiguration.Countdown timer
            ? ConfigurationAsync(timer with { DefaultDuration = TimeSpan.FromSeconds(seconds) })
            : Task.CompletedTask;

    private Task OverrideAsync(ChangeEventArgs args) =>
        _widget.Configuration is DashboardWidgetConfiguration.Automation a
            ? ConfigurationAsync(
                a with
                {
                    Data = args.Value is true
                        ? new DashboardTextOverride.Replace(string.Empty)
                        : new DashboardTextOverride.Inherit(),
                }
            )
            : Task.CompletedTask;

    private Task TextAsync(string text) =>
        _widget.Configuration is DashboardWidgetConfiguration.Automation a
            ? ConfigurationAsync(a with { Data = new DashboardTextOverride.Replace(text) })
            : Task.CompletedTask;

    private Task DictionaryAsync(ChangeEventArgs args) =>
        _widget.Configuration is DashboardWidgetConfiguration.Value v
            ? ConfigurationAsync(
                v with
                {
                    Selection = args.Value is true
                        ? new DashboardValueSelection.DictionaryEntry(string.Empty)
                        : new DashboardValueSelection.Scalar(),
                }
            )
            : Task.CompletedTask;

    private Task KeyAsync(string key) =>
        _widget.Configuration is DashboardWidgetConfiguration.Value v
            ? ConfigurationAsync(
                v with
                {
                    Selection = new DashboardValueSelection.DictionaryEntry(key),
                }
            )
            : Task.CompletedTask;

    private enum PlacementField
    {
        Column,
        Row,
        Width,
        Height,
    }

    private sealed record PlacementInput(PlacementField Kind, string Label, int Value, int Min);

    private IReadOnlyList<PlacementInput> _placementFields =>
        [
            new(PlacementField.Column, "Column", Placement.Column + 1, 1),
            new(PlacementField.Row, "Row", Placement.Row + 1, 1),
            new(PlacementField.Width, "Width (columns)", Placement.Width, 1),
            new(PlacementField.Height, "Height (rows)", Placement.Height, 1),
        ];

    private Task PlacementAsync(PlacementField field, int value)
    {
        if (value < 1)
        {
            return Task.CompletedTask;
        }
        var p = Placement;
        var changed = field switch
        {
            PlacementField.Column => p with { Column = value - 1 },
            PlacementField.Row => p with { Row = value - 1 },
            PlacementField.Width => p with { Width = value },
            PlacementField.Height => p with { Height = value },
        };
        return PlacementChanged.InvokeAsync(
            new DashboardPlacementTransport(
                changed.Column,
                changed.Row,
                changed.Width,
                changed.Height
            )
        );
    }
}
