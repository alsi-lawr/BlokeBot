using System.Globalization;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Persistence.Models;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Overlays;

public partial class OverlaysPage
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<OverlayInstanceView> _instances = [];
    private OverlayInstanceView? _selected;
    private Guid? _selectedId;
    private string _draftName = "Main stream overlay";
    private OverlayType _draftType = OverlayType.Guessing;
    private bool _draftShowGuessCount = true;
    private int _draftResultDurationSeconds = OverlayConfiguration
        .GuessingV1
        .DefaultResultDurationSeconds;
    private string _draftGiveawayTitle = OverlayConfiguration.GiveawayV1.DefaultTitle;
    private bool _draftShowEntrantCount = true;
    private bool _draftShowCountdown = true;
    private bool _draftShowJoinCommand = true;
    private int _eventFeedCapacity = OverlayConfiguration.EventFeedV1.DefaultCapacity;
    private EventFeedOverflowPolicy _eventFeedOverflow = EventFeedOverflowPolicy.DropNewest;
    private bool _pointEventEnabled = true;
    private string _pointEventTemplate = "{recipient} received {amount} {pointLabel}";
    private OverlayEventFeedPriority _pointEventPriority = OverlayEventFeedPriority.Normal;
    private int _pointEventDuration = 6;
    private bool _guessEventEnabled = true;
    private string _guessEventTemplate = "{winners} won {roundName}: {winningAnswer}";
    private OverlayEventFeedPriority _guessEventPriority = OverlayEventFeedPriority.High;
    private int _guessEventDuration = 8;
    private bool _giveawayEventEnabled = true;
    private string _giveawayEventTemplate = "{winners} won {prizes}";
    private OverlayEventFeedPriority _giveawayEventPriority = OverlayEventFeedPriority.High;
    private int _giveawayEventDuration = 8;
    private IReadOnlyList<PlayQueueSummary> _queueOptions = [];
    private int _viewerQueueId;
    private int _viewerQueueCurrentRows = OverlayConfiguration.ViewerQueueV1.DefaultCurrentRows;
    private int _viewerQueueNextRows = OverlayConfiguration.ViewerQueueV1.DefaultNextRows;
    private int _appearanceX = OverlayAppearance.GuessingDefault.X;
    private int _appearanceY = OverlayAppearance.GuessingDefault.Y;
    private int _appearanceWidth = OverlayAppearance.GuessingDefault.Width;
    private int _appearanceHeight = OverlayAppearance.GuessingDefault.Height;
    private string _appearanceCss = string.Empty;
    private string? _revealedBrowserSourceUrl;
    private string _feedback = string.Empty;
    private bool _operationFailed;
    private bool _featureEnabled;
    private bool _guessingFeatureEnabled;
    private bool _pointsFeatureEnabled;
    private bool _playWithViewersFeatureEnabled;
    private bool _isCreating = true;
    private bool _isLoading = true;
    private bool _isBusy;
    private OverlayPreviewMode _previewMode = OverlayPreviewMode.Live;
    private IJSObjectReference? _module;
    private DotNetObjectReference<OverlaysPage>? _selfReference;

    private string _previewUrl
    {
        get
        {
            if (_selected is null)
            {
                return string.Empty;
            }

            var mode =
                _previewMode is OverlayPreviewMode.Representative
                    ? "?mode=representative"
                    : string.Empty;
            if (
                _previewMode is OverlayPreviewMode.Representative
                && _selected.Type is OverlayType.Guessing
            )
            {
                mode = $"?mode=representative&sample={SampleToken(_previewSample)}";
            }
            if (
                _previewMode is OverlayPreviewMode.Representative
                && _selected.Type is OverlayType.Giveaway
            )
            {
                mode = $"?mode=representative&sample={SampleToken(_giveawayPreviewSample)}";
            }
            if (
                _previewMode is OverlayPreviewMode.Representative
                && _selected.Type is OverlayType.EventFeed
            )
            {
                mode = $"?mode=representative&sample={SampleToken(_eventFeedPreviewSample)}";
            }
            if (
                _previewMode is OverlayPreviewMode.Representative
                && _selected.Type is OverlayType.ViewerQueue
            )
            {
                mode = $"?mode=representative&sample={SampleToken(_viewerQueuePreviewSample)}";
            }
            return $"/overlays/preview/{_selected.Id:D}{mode}";
        }
    }

    private bool _selectedFeatureEnabled =>
        _selected?.Type switch
        {
            OverlayType.Guessing => _guessingFeatureEnabled,
            OverlayType.Giveaway => _pointsFeatureEnabled,
            OverlayType.ViewerQueue => _playWithViewersFeatureEnabled,
            _ => true,
        };

    private GuessingOverlaySampleState _previewSample = GuessingOverlaySampleState.Open;
    private GiveawayOverlaySampleState _giveawayPreviewSample = GiveawayOverlaySampleState.Open;
    private OverlayEventFeedKind _eventFeedPreviewSample = OverlayEventFeedKind.PointAward;
    private ViewerQueueOverlaySampleState _viewerQueuePreviewSample =
        ViewerQueueOverlaySampleState.Open;

    private string _presenceLabel
    {
        get
        {
            if (_selected is null)
            {
                return "No overlay selected.";
            }

            var connections = OtherConnectionCount(_selected);
            return connections switch
            {
                0 => "No other live Browser Source connections detected.",
                1 => "About 1 other live Browser Source connection detected.",
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"About {connections} other live Browser Source connections detected."
                ),
            };
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
        _ = RefreshPresenceAsync(_lifetime.Token);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                _module = await _js.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./Features/Overlays/OverlaysPage.razor.js"
                );
                _selfReference = DotNetObjectReference.Create(this);
            }
            if (_module is not null && _selfReference is not null)
            {
                await _module.InvokeVoidAsync("initializeAppearance", _selfReference);
            }
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private async Task LoadAsync(Guid? preferredOverlayId = null)
    {
        _isLoading = true;
        _operationFailed = false;
        try
        {
            if (Host is null)
            {
                return;
            }

            _featureEnabled = await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Overlays,
                CancellationToken.None
            );
            if (!_featureEnabled)
            {
                return;
            }
            _guessingFeatureEnabled = await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Guessing,
                CancellationToken.None
            );
            _pointsFeatureEnabled = await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Points,
                CancellationToken.None
            );
            _playWithViewersFeatureEnabled = await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.PlayWithViewers,
                CancellationToken.None
            );
            _queueOptions = _playWithViewersFeatureEnabled
                ? await _queues.GetQueuesForHostAsync(HostId, CancellationToken.None)
                : [];
            _viewerQueueId = _queueOptions.FirstOrDefault()?.Id ?? 0;

            var result = await _overlays.ListAsync(PageContext.Session, CancellationToken.None);
            _ = result.Match(
                succeeded =>
                {
                    _instances = succeeded.Value;
                    var selectId = preferredOverlayId ?? _selectedId;
                    var selected = selectId is null
                        ? _instances.FirstOrDefault()
                        : _instances.FirstOrDefault(value => value.Id == selectId);
                    if (selected is null)
                    {
                        NewOverlay(setFeedback: _instances.Count == 0);
                    }
                    else
                    {
                        SelectOverlay(selected, clearFeedback: false);
                    }
                    return true;
                },
                rejected =>
                {
                    SetFailure(rejected.Reason.Message);
                    return false;
                }
            );
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(LoadAsync), exception);
            SetFailure("Overlays could not load. Try again.");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void NewOverlay() => NewOverlay(setFeedback: true);

    private void NewOverlay(bool setFeedback)
    {
        _isCreating = true;
        _selected = null;
        _selectedId = null;
        _draftName = "Main stream overlay";
        _draftType = _guessingFeatureEnabled ? OverlayType.Guessing : OverlayType.Empty;
        _draftShowGuessCount = true;
        _draftResultDurationSeconds = OverlayConfiguration.GuessingV1.DefaultResultDurationSeconds;
        _draftGiveawayTitle = OverlayConfiguration.GiveawayV1.DefaultTitle;
        _draftShowEntrantCount = true;
        _draftShowCountdown = true;
        _draftShowJoinCommand = true;
        _viewerQueueCurrentRows = OverlayConfiguration.ViewerQueueV1.DefaultCurrentRows;
        _viewerQueueNextRows = OverlayConfiguration.ViewerQueueV1.DefaultNextRows;
        _viewerQueueId = _queueOptions.FirstOrDefault()?.Id ?? 0;
        LoadAppearance(DefaultAppearance(_draftType));
        _revealedBrowserSourceUrl = null;
        if (setFeedback)
        {
            SetSuccess(
                "New overlay ready. Name it, then select Create overlay. Nothing has been saved yet."
            );
        }
    }

    private void SelectOverlay(OverlayInstanceView overlay) =>
        SelectOverlay(overlay, clearFeedback: true);

    private void SelectOverlay(OverlayInstanceView overlay, bool clearFeedback)
    {
        _selected = overlay;
        _selectedId = overlay.Id;
        _draftName = overlay.Name;
        _draftType = overlay.Type;
        if (overlay.Configuration is OverlayConfiguration.GuessingV1 guessing)
        {
            _draftShowGuessCount = guessing.ShowGuessCount;
            _draftResultDurationSeconds = guessing.ResultDurationSeconds;
            LoadAppearance(guessing.Appearance);
        }
        if (overlay.Configuration is OverlayConfiguration.GiveawayV1 giveaway)
        {
            _draftGiveawayTitle = giveaway.Title;
            _draftShowEntrantCount = giveaway.ShowEntrantCount;
            _draftShowCountdown = giveaway.ShowCountdown;
            _draftShowJoinCommand = giveaway.ShowJoinCommand;
            LoadAppearance(giveaway.Appearance);
        }
        if (overlay.Configuration is OverlayConfiguration.EventFeedV1 feed)
        {
            _eventFeedCapacity = feed.Capacity;
            _eventFeedOverflow = feed.OverflowPolicy;
            LoadAppearance(feed.Appearance);
            LoadEventKind(
                feed.Kinds[OverlayEventFeedKind.PointAward],
                ref _pointEventEnabled,
                ref _pointEventTemplate,
                ref _pointEventPriority,
                ref _pointEventDuration
            );
            LoadEventKind(
                feed.Kinds[OverlayEventFeedKind.GuessingWinner],
                ref _guessEventEnabled,
                ref _guessEventTemplate,
                ref _guessEventPriority,
                ref _guessEventDuration
            );
            LoadEventKind(
                feed.Kinds[OverlayEventFeedKind.GiveawayWinner],
                ref _giveawayEventEnabled,
                ref _giveawayEventTemplate,
                ref _giveawayEventPriority,
                ref _giveawayEventDuration
            );
        }
        if (overlay.Configuration is OverlayConfiguration.ViewerQueueV1 queue)
        {
            _viewerQueueId = queue.QueueId;
            _viewerQueueCurrentRows = queue.CurrentRows;
            _viewerQueueNextRows = queue.NextRows;
            LoadAppearance(queue.Appearance);
        }
        _isCreating = false;
        _revealedBrowserSourceUrl = null;
        if (clearFeedback)
        {
            _feedback = string.Empty;
            _operationFailed = false;
        }
    }

    private Task SaveAsync() =>
        RunOperationAsync(async () =>
        {
            OverlayConfiguration configuration;
            try
            {
                configuration = DraftConfiguration();
            }
            catch (ArgumentException exception)
            {
                SetFailure(exception.Message);
                return;
            }

            if (_isCreating)
            {
                var creationResult = await _overlays.CreateAsync(
                    PageContext.Session,
                    new CreateOverlayInstanceCommand(_draftName, _draftType, configuration),
                    CancellationToken.None
                );
                await creationResult.Match(
                    async succeeded =>
                    {
                        var revealedUrl = AbsoluteUrl(succeeded.Value.PrivateAccess.RelativeUrl);
                        await LoadAsync(succeeded.Value.Instance.Id);
                        _revealedBrowserSourceUrl = revealedUrl;
                        SetSuccess("Overlay created. Copy the private Browser Source URL now.");
                    },
                    rejected =>
                    {
                        SetFailure(rejected.Reason.Message);
                        return Task.CompletedTask;
                    }
                );
                return;
            }

            if (_selected is null)
            {
                SetFailure("Choose an overlay or create a new one.");
                return;
            }

            if (!_selectedFeatureEnabled)
            {
                SetFailure(
                    _selected.Type switch
                    {
                        OverlayType.Giveaway =>
                            "This giveaway overlay is paused. Turn Points on in Channel setup before changing it.",
                        OverlayType.ViewerQueue =>
                            "This Viewer Queue overlay is paused. Turn Play with viewers on in Channel setup before changing it.",
                        _ =>
                            "This guessing overlay is paused. Turn Guessing game on in Channel setup before changing it.",
                    }
                );
                return;
            }

            var current = _selected;
            if (!string.Equals(current.Name, _draftName.Trim(), StringComparison.Ordinal))
            {
                var renamed = await _overlays.RenameAsync(
                    PageContext.Session,
                    new RenameOverlayInstanceCommand(current.Id, current.Revision, _draftName),
                    CancellationToken.None
                );
                if (renamed is OverlayInstanceResult<OverlayInstanceView>.Rejected rejected)
                {
                    SetFailure(rejected.Reason.Message);
                    return;
                }
                current = ((OverlayInstanceResult<OverlayInstanceView>.Succeeded)renamed).Value;
            }

            if (configuration != current.Configuration)
            {
                var configured = await _overlays.ConfigureAsync(
                    PageContext.Session,
                    new ConfigureOverlayInstanceCommand(
                        current.Id,
                        current.Revision,
                        configuration
                    ),
                    CancellationToken.None
                );
                if (configured is OverlayInstanceResult<OverlayInstanceView>.Rejected rejected)
                {
                    SetFailure(rejected.Reason.Message);
                    return;
                }
                current = ((OverlayInstanceResult<OverlayInstanceView>.Succeeded)configured).Value;
            }

            SetSuccess("Overlay saved.");
            await LoadAsync(current.Id);
        });

    private async Task ToggleAvailabilityAsync()
    {
        if (_selected is null)
        {
            return;
        }

        if (
            _selected.IsEnabled
            && !await ConfirmAsync(
                "Disable this overlay? Its Browser Source and live updates will stop until it is enabled again."
            )
        )
        {
            SetSuccess("Disable cancelled. The overlay is still enabled.");
            return;
        }

        await RunOperationAsync(async () =>
        {
            var selected = _selected;
            if (selected is null)
            {
                return;
            }

            var command = new ChangeOverlayInstanceAvailabilityCommand(
                selected.Id,
                selected.Revision
            );
            var result = selected.IsEnabled
                ? await _overlays.DisableAsync(PageContext.Session, command, CancellationToken.None)
                : await _overlays.EnableAsync(PageContext.Session, command, CancellationToken.None);
            await result.Match(
                async succeeded =>
                {
                    SetSuccess(
                        succeeded.Value.IsEnabled
                            ? "Overlay enabled."
                            : "Overlay disabled. Its Browser Source is unavailable."
                    );
                    await LoadAsync(succeeded.Value.Id);
                },
                rejected =>
                {
                    SetFailure(rejected.Reason.Message);
                    return Task.CompletedTask;
                }
            );
        });
    }

    private async Task RotateUrlAsync()
    {
        if (
            _selected is null
            || !await ConfirmAsync(
                "Rotate this overlay's private URL? Every existing OBS source using the old URL will stop working."
            )
        )
        {
            if (_selected is not null)
            {
                SetSuccess("URL rotation cancelled. The existing Browser Source URL still works.");
            }
            return;
        }

        await RunOperationAsync(async () =>
        {
            var selected = _selected;
            if (selected is null)
            {
                return;
            }

            var result = await _overlays.RotateKeyAsync(
                PageContext.Session,
                new RotateOverlayInstanceKeyCommand(selected.Id, selected.Revision),
                CancellationToken.None
            );
            await result.Match(
                async succeeded =>
                {
                    var revealedUrl = AbsoluteUrl(succeeded.Value.PrivateAccess.RelativeUrl);
                    await LoadAsync(succeeded.Value.Instance.Id);
                    _revealedBrowserSourceUrl = revealedUrl;
                    SetSuccess(
                        "Private URL rotated. Copy the replacement now and update every OBS source."
                    );
                },
                rejected =>
                {
                    SetFailure(rejected.Reason.Message);
                    return Task.CompletedTask;
                }
            );
        });
    }

    private async Task DeleteAsync()
    {
        if (
            _selected is null
            || !await ConfirmAsync(
                "Delete this overlay permanently? Its private URL and every OBS source using it will stop working."
            )
        )
        {
            if (_selected is not null)
            {
                SetSuccess("Delete cancelled. The overlay was not changed.");
            }
            return;
        }

        await RunOperationAsync(async () =>
        {
            var selected = _selected;
            if (selected is null)
            {
                return;
            }

            var result = await _overlays.DeleteAsync(
                PageContext.Session,
                new DeleteOverlayInstanceCommand(selected.Id, selected.Revision),
                CancellationToken.None
            );
            await result.Match(
                async _ =>
                {
                    _selected = null;
                    _selectedId = null;
                    _revealedBrowserSourceUrl = null;
                    await LoadAsync();
                    SetSuccess("Overlay deleted. Its Browser Source URL no longer works.");
                },
                rejected =>
                {
                    SetFailure(rejected.Reason.Message);
                    return Task.CompletedTask;
                }
            );
        });
    }

    private Task PublishTestAsync() =>
        RunOperationAsync(async () =>
        {
            if (_selected is null || !_selectedFeatureEnabled)
            {
                return;
            }

            var result = await _overlays.GetAsync(
                PageContext.Session,
                _selected.Id,
                CancellationToken.None
            );
            _ = result.Match(
                succeeded =>
                {
                    if (!succeeded.Value.IsEnabled)
                    {
                        SetFailure("Enable the overlay before sending a test pulse.");
                        return false;
                    }
                    if (_presence.Read(HostId, succeeded.Value.Id).ActiveConnectionCount == 0)
                    {
                        SetFailure(
                            "No Browser Source is connected. Select Live preview or connect the Browser Source in OBS, then try again."
                        );
                        return false;
                    }

                    _publisher.PublishTest(
                        new ResolvedOverlayInstance(
                            HostId,
                            succeeded.Value.Id,
                            succeeded.Value.Type,
                            succeeded.Value.Configuration,
                            succeeded.Value.Revision
                        )
                    );
                    SetSuccess(
                        "Test pulse published only to this overlay. It is temporary and changes no stream or chat data."
                    );
                    return true;
                },
                rejected =>
                {
                    SetFailure(rejected.Reason.Message);
                    return false;
                }
            );
        });

    private async Task CopyBrowserSourceUrlAsync()
    {
        if (_revealedBrowserSourceUrl is null)
        {
            SetFailure("Create or rotate the overlay before copying its private URL.");
            return;
        }

        try
        {
            if (_module is null)
            {
                throw new JSException("Clipboard support is not ready.");
            }

            await _module.InvokeVoidAsync("copyText", _revealedBrowserSourceUrl);
            SetSuccess("Private Browser Source URL copied.");
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException)
        {
            SetFailure("The URL could not be copied. Select the URL and copy it manually.");
        }
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (_isBusy || Host is null)
        {
            return;
        }

        _isBusy = true;
        try
        {
            await RunSelectedHostMutationAsync(HostId, operation);
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(RunOperationAsync), exception);
            SetFailure("The overlay operation failed. Try again.");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task<bool> ConfirmAsync(string prompt)
    {
        try
        {
            return await _js.InvokeAsync<bool>("confirm", [prompt]);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException)
        {
            SetFailure("Confirmation could not open. No changes were made.");
            return false;
        }
    }

    private void SetPreviewMode(OverlayPreviewMode mode)
    {
        _previewMode = mode;
        SetSuccess(
            mode is OverlayPreviewMode.Live
                ? "Live preview selected."
                : "Representative sample selected. This mode does not open a live connection."
        );
    }

    private void SetPreviewSample(GuessingOverlaySampleState sample)
    {
        _previewSample = sample;
        _previewMode = OverlayPreviewMode.Representative;
        SetSuccess($"{SampleLabel(sample)} sample selected.");
    }

    private void SetPreviewSample(GiveawayOverlaySampleState sample)
    {
        _giveawayPreviewSample = sample;
        _previewMode = OverlayPreviewMode.Representative;
        SetSuccess($"{SampleLabel(sample)} sample selected.");
    }

    private void SetPreviewSample(OverlayEventFeedKind sample)
    {
        _eventFeedPreviewSample = sample;
        _previewMode = OverlayPreviewMode.Representative;
        SetSuccess($"{EventKindLabel(sample)} sample selected.");
    }

    private void SetPreviewSample(ViewerQueueOverlaySampleState sample)
    {
        _viewerQueuePreviewSample = sample;
        _previewMode = OverlayPreviewMode.Representative;
        SetSuccess($"{SampleLabel(sample)} sample selected.");
    }

    private OverlayConfiguration DraftConfiguration() =>
        _draftType switch
        {
            OverlayType.Empty => new OverlayConfiguration.EmptyV1(),
            OverlayType.Guessing => new OverlayConfiguration.GuessingV1(
                _draftShowGuessCount,
                _draftResultDurationSeconds,
                DraftAppearance()
            ),
            OverlayType.CuePlayer => new OverlayConfiguration.CuePlayerV1(),
            OverlayType.Giveaway => new OverlayConfiguration.GiveawayV1(
                _draftGiveawayTitle,
                _draftShowEntrantCount,
                _draftShowCountdown,
                _draftShowJoinCommand,
                DraftAppearance()
            ),
            OverlayType.EventFeed => new OverlayConfiguration.EventFeedV1(
                _eventFeedCapacity,
                _eventFeedOverflow,
                new Dictionary<OverlayEventFeedKind, EventFeedKindConfiguration>
                {
                    [OverlayEventFeedKind.PointAward] = new(
                        _pointEventEnabled,
                        _pointEventTemplate,
                        _pointEventPriority,
                        _pointEventDuration
                    ),
                    [OverlayEventFeedKind.GuessingWinner] = new(
                        _guessEventEnabled,
                        _guessEventTemplate,
                        _guessEventPriority,
                        _guessEventDuration
                    ),
                    [OverlayEventFeedKind.GiveawayWinner] = new(
                        _giveawayEventEnabled,
                        _giveawayEventTemplate,
                        _giveawayEventPriority,
                        _giveawayEventDuration
                    ),
                },
                DraftAppearance()
            ),
            OverlayType.ViewerQueue => new OverlayConfiguration.ViewerQueueV1(
                _viewerQueueId,
                _viewerQueueCurrentRows,
                _viewerQueueNextRows,
                DraftAppearance()
            ),
            _ => throw new InvalidOperationException("The selected overlay type is unsupported."),
        };

    private OverlayAppearance DraftAppearance() =>
        new(_appearanceX, _appearanceY, _appearanceWidth, _appearanceHeight, _appearanceCss);

    private void LoadAppearance(OverlayAppearance appearance)
    {
        _appearanceX = appearance.X;
        _appearanceY = appearance.Y;
        _appearanceWidth = appearance.Width;
        _appearanceHeight = appearance.Height;
        _appearanceCss = appearance.Css;
    }

    private static OverlayAppearance DefaultAppearance(OverlayType type) =>
        type switch
        {
            OverlayType.Guessing => OverlayAppearance.GuessingDefault,
            OverlayType.Giveaway => OverlayAppearance.GiveawayDefault,
            OverlayType.EventFeed => OverlayAppearance.EventFeedDefault,
            OverlayType.ViewerQueue => OverlayAppearance.ViewerQueueDefault,
            _ => OverlayAppearance.GuessingDefault,
        };

    private void ResetAppearance() => LoadAppearance(DefaultAppearance(_draftType));

    [JSInvokable]
    public string? ScopeDraftCss(string css) =>
        OverlayAppearance.ValidateCss(css) is not null
            ? null
            : new OverlayAppearance(
                _appearanceX,
                _appearanceY,
                _appearanceWidth,
                _appearanceHeight,
                css
            ).ToScopedCss();

    [JSInvokable]
    public void UpdateAppearance(int x, int y, int width, int height)
    {
        try
        {
            var appearance = new OverlayAppearance(x, y, width, height, _appearanceCss);
            LoadAppearance(appearance);
        }
        catch (ArgumentOutOfRangeException) { }
    }

    private string AbsoluteUrl(string relativeUrl) =>
        _navigation.ToAbsoluteUri(relativeUrl).AbsoluteUri;

    private void SetSuccess(string message)
    {
        _feedback = message;
        _operationFailed = false;
    }

    private void SetFailure(string message)
    {
        _feedback = message;
        _operationFailed = true;
    }

    private static string CountLabel(int count) =>
        count == 1 ? "1 saved overlay" : $"{count} saved overlays";

    private static string CueCountLabel(int count) =>
        count == 1 ? "1 saved cue" : $"{count} saved cues";

    private static string UpdatedLabel(OverlayInstanceView overlay) =>
        $"updated {overlay.UpdatedAtUtc:yyyy-MM-dd HH:mm} UTC";

    private static string TypeLabel(OverlayType type) =>
        type switch
        {
            OverlayType.Empty => "Empty",
            OverlayType.Guessing => "Guessing",
            OverlayType.CuePlayer => "Cue player",
            OverlayType.Giveaway => "Giveaway",
            OverlayType.EventFeed => "Event feed",
            OverlayType.ViewerQueue => "Viewer Queue",
            _ => "Unsupported",
        };

    private static void LoadEventKind(
        EventFeedKindConfiguration source,
        ref bool enabled,
        ref string template,
        ref OverlayEventFeedPriority priority,
        ref int duration
    )
    {
        enabled = source.Enabled;
        template = source.Template;
        priority = source.Priority;
        duration = source.DurationSeconds;
    }

    private static string SampleLabel(GuessingOverlaySampleState sample) =>
        sample switch
        {
            GuessingOverlaySampleState.NoRound => "No round",
            GuessingOverlaySampleState.Open => "Open",
            GuessingOverlaySampleState.Closed => "Closed",
            GuessingOverlaySampleState.Completed => "Result",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };

    private static string SampleToken(OverlayEventFeedKind kind) =>
        kind switch
        {
            OverlayEventFeedKind.PointAward => "point-award",
            OverlayEventFeedKind.GuessingWinner => "guessing-winner",
            OverlayEventFeedKind.GiveawayWinner => "giveaway-winner",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string EventKindLabel(OverlayEventFeedKind kind) =>
        kind switch
        {
            OverlayEventFeedKind.PointAward => "Point award",
            OverlayEventFeedKind.GuessingWinner => "Guessing winner",
            OverlayEventFeedKind.GiveawayWinner => "Giveaway winner",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string SampleLabel(ViewerQueueOverlaySampleState sample) =>
        sample switch
        {
            ViewerQueueOverlaySampleState.Open => "Open",
            ViewerQueueOverlaySampleState.Closed => "Closed",
            ViewerQueueOverlaySampleState.PartyChanged => "Party changed",
            ViewerQueueOverlaySampleState.ReadyOutcome => "Ready outcome",
            ViewerQueueOverlaySampleState.SelectedNext => "Selected next",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };

    private static string SampleToken(ViewerQueueOverlaySampleState sample) =>
        sample switch
        {
            ViewerQueueOverlaySampleState.Open => "open",
            ViewerQueueOverlaySampleState.Closed => "closed",
            ViewerQueueOverlaySampleState.PartyChanged => "party-changed",
            ViewerQueueOverlaySampleState.ReadyOutcome => "ready-outcome",
            ViewerQueueOverlaySampleState.SelectedNext => "selected-next",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };

    private static string SampleToken(GuessingOverlaySampleState sample) =>
        sample switch
        {
            GuessingOverlaySampleState.NoRound => "no-round",
            GuessingOverlaySampleState.Open => "open",
            GuessingOverlaySampleState.Closed => "closed",
            GuessingOverlaySampleState.Completed => "completed",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };

    private static string SampleLabel(GiveawayOverlaySampleState sample) =>
        sample switch
        {
            GiveawayOverlaySampleState.Idle => "No current giveaway",
            GiveawayOverlaySampleState.Open => "Open",
            GiveawayOverlaySampleState.Ending => "Ending",
            GiveawayOverlaySampleState.Completed => "Winners",
            GiveawayOverlaySampleState.Cancelled => "Cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };

    private static string SampleToken(GiveawayOverlaySampleState sample) =>
        sample switch
        {
            GiveawayOverlaySampleState.Idle => "idle",
            GiveawayOverlaySampleState.Open => "open",
            GiveawayOverlaySampleState.Ending => "ending",
            GiveawayOverlaySampleState.Completed => "completed",
            GiveawayOverlaySampleState.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };

    private string InventoryPresenceLabel(OverlayInstanceView overlay) =>
        OtherConnectionCount(overlay) > 0
            ? "Browser Source connected"
            : "No other Browser Source connected";

    private int OtherConnectionCount(OverlayInstanceView overlay)
    {
        var active = _presence.Read(HostId, overlay.Id).ActiveConnectionCount;
        var includesThisPreview =
            _previewMode is OverlayPreviewMode.Live && _selected?.Id == overlay.Id && !_isCreating;
        return Math.Max(0, active - (includesThisPreview ? 1 : 0));
    }

    private async Task RefreshPresenceAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            _selfReference?.Dispose();
        }

        base.Dispose(disposing);
    }

    private string PreviewButtonClass(OverlayPreviewMode mode) =>
        _previewMode == mode
            ? "segmented-motion__tab segmented-motion__tab--active"
            : "segmented-motion__tab";

    private enum OverlayPreviewMode
    {
        Live,
        Representative,
    }
}
