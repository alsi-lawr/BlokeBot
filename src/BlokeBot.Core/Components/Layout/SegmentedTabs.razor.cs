using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace BlokeBot.Core.Components.Layout;

public sealed record SegmentedTabItem(string Key, string Label, string? Href = null);

public partial class SegmentedTabs : IDisposable
{
    [Inject]
    private NavigationManager _navigation { get; set; } = null!;

    [Parameter, EditorRequired]
    public required string AriaLabel { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyList<SegmentedTabItem> Items { get; set; }

    [Parameter]
    public string? ActiveKey { get; set; }

    [Parameter]
    public EventCallback<string> ActiveKeyChanged { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string _style =>
        $"--segmented-count: {Math.Max(Items.Count, 1)}; --segmented-active-index: {_activeIndex};";

    private int _activeIndex
    {
        get
        {
            for (var index = 0; index < Items.Count; index++)
            {
                if (IsActive(Items[index]))
                {
                    return index;
                }
            }

            return 0;
        }
    }

    protected override void OnInitialized() => _navigation.LocationChanged += HandleLocationChanged;

    private bool IsActive(SegmentedTabItem item) =>
        item.Href is null
            ? string.Equals(item.Key, ActiveKey, StringComparison.Ordinal)
            : PathsMatch(item.Href);

    private bool PathsMatch(string href)
    {
        var target = _navigation.ToAbsoluteUri(href);
        var current = _navigation.ToAbsoluteUri(_navigation.Uri);
        return string.Equals(
            target.AbsolutePath.TrimEnd('/'),
            current.AbsolutePath.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private async Task SelectAsync(SegmentedTabItem item)
    {
        if (item.Href is null && !IsActive(item))
        {
            await ActiveKeyChanged.InvokeAsync(item.Key);
        }
    }

    private static string TabClass(bool active) =>
        active ? "segmented-motion__tab segmented-motion__tab--active" : "segmented-motion__tab";

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(StateHasChanged);

    public void Dispose() => _navigation.LocationChanged -= HandleLocationChanged;
}
