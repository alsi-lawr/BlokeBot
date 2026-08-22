using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace BlokeBot.Core.Components.Layout;

/// <summary>
/// Owns the canonical URL fragment of a fragment-routed page: bare or unknown fragments are
/// replaced with the first key, selections made on the page are pushed as one history entry, and
/// the current selection is republished to <see cref="DashboardFragmentState"/>.
/// </summary>
/// <remarks>
/// Same-page fragment pushes never raise the server-side location notification, so a selection made
/// through <see cref="Select"/> is remembered and the equivalent notification bunit raises is
/// discarded. The change callback therefore reports only browser-driven fragment movement such as
/// back and forward. Location notifications reach this type on the caller's thread, so the
/// dispatcher supplied at construction (a component's <c>InvokeAsync</c>) marshals the reaction.
/// </remarks>
public sealed class DashboardFragmentOwner : IDisposable
{
    private readonly NavigationManager _navigation;
    private readonly DashboardFragmentState _state;
    private readonly IReadOnlyList<string> _keys;
    private readonly Func<Func<Task>, Task> _dispatch;
    private readonly Func<string, Task> _selectionChanged;
    private readonly string _ownedUri;
    private readonly string _path;
    private string _selected;

    public DashboardFragmentOwner(
        NavigationManager navigation,
        DashboardFragmentState state,
        IReadOnlyList<string> keys,
        string selected,
        Func<Func<Task>, Task> dispatch,
        Func<string, Task> selectionChanged
    )
    {
        _navigation = navigation;
        _state = state;
        _keys = keys;
        _selected = selected;
        _dispatch = dispatch;
        _selectionChanged = selectionChanged;
        var currentUri = navigation.ToAbsoluteUri(navigation.Uri);
        _ownedUri = currentUri.GetLeftPart(UriPartial.Query);
        _path = currentUri.AbsolutePath;
        navigation.LocationChanged += HandleLocationChanged;
        Normalize();
    }

    public static string CanonicalKey(NavigationManager navigation, IReadOnlyList<string> keys) =>
        keys.FirstOrDefault(key =>
            string.Equals(key, Fragment(navigation), StringComparison.Ordinal)
        ) ?? keys[0];

    public string Canonical => CanonicalKey(_navigation, _keys);

    public void Publish(string selected)
    {
        _selected = selected;
        _state.Set(_path, selected);
    }

    public void Select(string key)
    {
        _selected = key;
        _navigation.NavigateTo(UriFor(key));
    }

    public void Dispose() => _navigation.LocationChanged -= HandleLocationChanged;

    private static string Fragment(NavigationManager navigation) =>
        navigation.ToAbsoluteUri(navigation.Uri).Fragment.TrimStart('#');

    private bool _onOwnedPath =>
        string.Equals(
            _navigation.ToAbsoluteUri(_navigation.Uri).AbsolutePath,
            _path,
            StringComparison.Ordinal
        );

    internal string UriFor(string key)
    {
        var currentUri = _navigation.ToAbsoluteUri(_navigation.Uri);
        var pageUri = _onOwnedPath ? currentUri.GetLeftPart(UriPartial.Query) : _ownedUri;
        return pageUri + "#" + key;
    }

    internal string HrefFor(string key) => _onOwnedPath ? $"#{key}" : UriFor(key);

    private void Normalize()
    {
        if (!_onOwnedPath)
        {
            return;
        }

        var canonical = Canonical;
        if (!string.Equals(Fragment(_navigation), canonical, StringComparison.Ordinal))
        {
            _navigation.NavigateTo(UriFor(canonical), replace: true);
        }
    }

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = _dispatch(() =>
        {
            if (!_onOwnedPath)
            {
                return Task.CompletedTask;
            }

            Normalize();
            var canonical = Canonical;
            if (string.Equals(canonical, _selected, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            _selected = canonical;
            return _selectionChanged(canonical);
        });
}
