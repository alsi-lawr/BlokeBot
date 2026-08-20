using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
    private const string _popoverId = "page-help-popover";
    private const string _titleId = "page-help-title";

    private bool _isOpen;
    private bool _restoreFocus;
    private ElementReference _trigger;

    private HelpLocation? _currentLocation => LocationFor(_currentPath, _currentFragment);

    private HelpPage? _currentHelp => _currentLocation?.Help;

    private Uri? _guideUri =>
        HelpSiteGuide.Resolve(_options.Value.HelpSiteBaseUrl, _currentLocation?.GuidePath);

    private string _currentPath
    {
        get
        {
            var relative = _navigation.ToBaseRelativePath(_navigation.Uri);
            var path = relative.Split('?', '#')[0].Trim('/');
            return string.IsNullOrWhiteSpace(path) ? "/" : "/" + path;
        }
    }

    // Same-page fragment pushes never raise LocationChanged on the server, so the
    // fragment-owned tab state is the authority whenever it describes the current path.
    private string _currentFragment =>
        string.Equals(_fragments.Path, _currentAbsolutePath, StringComparison.Ordinal)
        && _fragments.Fragment is { } fragment
            ? fragment
            : _navigation.ToAbsoluteUri(_navigation.Uri).Fragment.TrimStart('#');

    private string _currentAbsolutePath => _navigation.ToAbsoluteUri(_navigation.Uri).AbsolutePath;

    protected override void OnInitialized()
    {
        _navigation.LocationChanged += OnLocationChanged;
        _fragments.Changed += OnFragmentChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_restoreFocus)
        {
            _restoreFocus = false;
            await _trigger.FocusAsync();
        }
    }

    public void Dispose()
    {
        _navigation.LocationChanged -= OnLocationChanged;
        _fragments.Changed -= OnFragmentChanged;
    }

    private void OnFragmentChanged() =>
        _ = InvokeAsync(() =>
        {
            _isOpen = false;
            StateHasChanged();
        });

    private void CloseAndRestoreFocus()
    {
        _isOpen = false;
        _restoreFocus = true;
    }

    private void Toggle() => _isOpen = !_isOpen;

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (_isOpen && args.Key == "Escape")
        {
            CloseAndRestoreFocus();
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(() =>
        {
            _isOpen = false;
            StateHasChanged();
        });

    /// <summary>
    /// Every dashboard location that has page help, paired with the BlokeBot.Site guide it points
    /// at. Pairing them here keeps a route from gaining help without a guide destination.
    /// </summary>
    private static HelpLocation? LocationFor(string path, string fragment) =>
        path switch
        {
            "/" => new(_homeHelp, "/dashboard"),
            "/admin" => new(_adminHelp, "/channels"),
            "/alerts" => new(_alertsHelp, "/troubleshooting"),
            "/guessing" => new(_guessingDashboardHelp, "/guessing"),
            "/guessing/settings" => new(_guessingSettingsHelp, "/guessing"),
            "/points" => new(_pointsDashboardHelp, "/points"),
            "/points/settings" => new(_pointsSettingsHelp, "/points"),
            "/custom-commands/settings" => new(_customCommandsHelp, "/commands"),
            "/automations" => new(_automationsHelp, "/automations"),
            "/automations/events" => new(_automationEventsHelp, "/automations/events"),
            "/host" => new(_hostConfigHelp, "/channels"),
            "/configuration-transfer" => new(_configurationTransferHelp, "/channels"),
            "/requests" => new(_requestBoardsHelp, "/community/request-boards"),
            "/bounties" => new(_bountiesHelp, "/community/bounties"),
            "/community" => new(_communityProgressionHelp, "/community/progression"),
            "/raid" => new(_blokeRaidHelp, "/community/blokeraid"),
            "/passports" => new(_viewerPassportsHelp, "/community/passports"),
            _ when path.StartsWith("/passports/", StringComparison.Ordinal)
                    && path.EndsWith("/me", StringComparison.Ordinal) => new(
                _viewerPassportsHelp,
                "/community/passports"
            ),
            "/bingo" => new(_bingoHelp, "/community/bingo"),
            "/competitions" => new(_competitionsHelp, "/community/competitions"),
            "/raid-collaboration" => new(_raidCollaborationHelp, "/community/raid-collaboration"),
            "/collectives" => new(_collectivesHelp, "/community/collectives"),
            "/queues" => new(_playQueuesHelp, "/community/play-with-viewers"),
            "/moments" => new(_momentsHelp, "/community/moments"),
            "/overlays" => fragment switch
            {
                "cues" => new(_cuesHelp, "/overlays/cues"),
                "media" => new(_mediaLibraryHelp, "/overlays/media"),
                _ => new(_overlaysHelp, "/overlays"),
            },
            "/twitch-operations/polls" => new(_pollsHelp, "/twitch-operations/polls"),
            "/twitch-operations/clips-markers" => new(
                _clipsMarkersHelp,
                "/twitch-operations/clips-markers"
            ),
            "/twitch-operations/channel-points" => new(
                _channelPointsHelp,
                "/twitch-operations/channel-points"
            ),
            "/twitch-operations/predictions" => new(
                _predictionsHelp,
                "/twitch-operations/predictions"
            ),
            _ => null,
        };

    internal static string? GuidePathForLocation(string path, string fragment) =>
        LocationFor(path, fragment)?.GuidePath;

    internal static bool HasUsefulHelpForPath(string path) =>
        LocationFor(path, string.Empty)?.Help is { } help
        && !string.IsNullOrWhiteSpace(help.Title)
        && help.Sections.Count > 0
        && help.Sections.All(static section =>
            !string.IsNullOrWhiteSpace(section.Title)
            && (
                !string.IsNullOrWhiteSpace(section.Body)
                || section.Items.Any(static item => !string.IsNullOrWhiteSpace(item))
            )
        );

    private sealed record HelpPage(string Title, IReadOnlyList<HelpSection> Sections);

    private sealed record HelpSection(string Title, string Body, IReadOnlyList<string> Items);

    private sealed record HelpLocation(HelpPage Help, string GuidePath);
}
