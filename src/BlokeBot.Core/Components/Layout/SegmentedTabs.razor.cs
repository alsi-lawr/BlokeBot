using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Components.Layout;

public sealed record SegmentedTabItem(string Key, string Label, string? Href = null);

public partial class SegmentedTabs : IDisposable
{
    [Inject]
    private NavigationManager _navigation { get; set; } = null!;

    [Inject]
    private DashboardFragmentState _fragmentState { get; set; } = null!;

    [Parameter, EditorRequired]
    public required string AriaLabel { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyList<SegmentedTabItem> Items { get; set; }

    [Parameter]
    public RenderFragment<SegmentedTabItem>? ItemTemplate { get; set; }

    [Parameter]
    public string? Id { get; set; }

    /// <summary>
    /// Makes this strip the owner of the page's URL fragment. Fragment-owned tabs render as
    /// anchors so a workspace pane can be linked, refreshed, and reached with Back and Forward.
    /// </summary>
    [Parameter]
    public bool OwnsFragment { get; set; }

    [Parameter]
    public string? ActiveKey { get; set; }

    [Parameter]
    public EventCallback<string> ActiveKeyChanged { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private readonly Dictionary<string, ElementReference> _tabElements = [];
    private string? _pendingFocusKey;
    private DashboardFragmentOwner? _fragment;

    public static string TabId(string id, string key) => $"{id}-{key}-tab";

    public static string PanelId(string id, string key) => $"{id}-{key}-panel";

    public static string CanonicalKey(
        NavigationManager navigation,
        IReadOnlyList<SegmentedTabItem> items
    ) => DashboardFragmentOwner.CanonicalKey(navigation, [.. items.Select(item => item.Key)]);

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

    protected override void OnInitialized()
    {
        _navigation.LocationChanged += HandleLocationChanged;
        if (OwnsFragment)
        {
            _fragment = new(
                _navigation,
                _fragmentState,
                [.. Items.Select(item => item.Key)],
                _selectedFragmentKey,
                InvokeAsync,
                ActiveKeyChanged.InvokeAsync
            );
        }
    }

    protected override void OnParametersSet() => _fragment?.Publish(_selectedFragmentKey);

    // The browser skips the server-side LocationChanged notification for same-page fragment
    // pushes, so the parent's ActiveKey is the source of truth after the initial navigation.
    private string _selectedFragmentKey =>
        ActiveKey is { } key
        && Items.Any(item => string.Equals(item.Key, key, StringComparison.Ordinal))
            ? key
            : CanonicalKey(_navigation, Items);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusKey is { } key && _tabElements.TryGetValue(key, out var element))
        {
            _pendingFocusKey = null;
            await element.FocusAsync();
        }
    }

    private bool IsActive(SegmentedTabItem item) =>
        OwnsFragment ? string.Equals(item.Key, _selectedFragmentKey, StringComparison.Ordinal)
        : item.Href is null ? string.Equals(item.Key, ActiveKey, StringComparison.Ordinal)
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
        if (IsActive(item))
        {
            return;
        }

        if (_fragment is { } fragment)
        {
            await ActiveKeyChanged.InvokeAsync(item.Key);
            fragment.Select(item.Key);
            return;
        }

        if (item.Href is null)
        {
            await ActiveKeyChanged.InvokeAsync(item.Key);
        }
        else
        {
            _navigation.NavigateTo(item.Href);
        }
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs args, SegmentedTabItem item)
    {
        var index = IndexOf(item);
        var target = args.Key switch
        {
            "ArrowLeft" or "ArrowUp" => Items[(index - 1 + Items.Count) % Items.Count],
            "ArrowRight" or "ArrowDown" => Items[(index + 1) % Items.Count],
            "Home" => Items[0],
            "End" => Items[^1],
            _ => null,
        };
        if (target is null || (target == item && args.Key is not ("Home" or "End")))
        {
            return;
        }

        _pendingFocusKey = target.Key;
        await SelectAsync(target);
    }

    private int IndexOf(SegmentedTabItem item)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index] == item)
            {
                return index;
            }
        }

        return 0;
    }

    private string FragmentHref(SegmentedTabItem item) =>
        _fragment?.UriFor(item.Key) ?? $"#{item.Key}";

    private string? TabIdFor(SegmentedTabItem item) => Id is null ? null : TabId(Id, item.Key);

    private string? PanelIdFor(SegmentedTabItem item) => Id is null ? null : PanelId(Id, item.Key);

    private int TabIndexFor(SegmentedTabItem item) => IsActive(item) ? 0 : -1;

    private static string TabClass(bool active) =>
        active ? "segmented-motion__tab segmented-motion__tab--active" : "segmented-motion__tab";

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        _navigation.LocationChanged -= HandleLocationChanged;
        _fragment?.Dispose();
    }
}
