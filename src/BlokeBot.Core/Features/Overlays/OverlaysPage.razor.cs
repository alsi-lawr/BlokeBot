using System.Globalization;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
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
    private string? _revealedBrowserSourceUrl;
    private string _feedback = string.Empty;
    private bool _operationFailed;
    private bool _featureEnabled;
    private bool _guessingFeatureEnabled;
    private bool _isCreating = true;
    private bool _isLoading = true;
    private bool _isBusy;
    private long _previewKey;
    private OverlayPreviewMode _previewMode = OverlayPreviewMode.Live;
    private IJSObjectReference? _module;

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
            return $"/overlays/preview/{_selected.Id:D}{mode}";
        }
    }

    private bool _selectedFeatureEnabled =>
        _selected?.Type is not OverlayType.Guessing || _guessingFeatureEnabled;

    private GuessingOverlaySampleState _previewSample = GuessingOverlaySampleState.Open;

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
        await LoadPageContextAsync();
        await LoadAsync();
        _ = RefreshPresenceAsync(_lifetime.Token);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Features/Overlays/OverlaysPage.razor.js"
            );
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

            var result = await _overlays.ListAsync(PageContext.Session, CancellationToken.None);
            result.Match(
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

    private void NewOverlay()
    {
        NewOverlay(setFeedback: true);
    }

    private void NewOverlay(bool setFeedback)
    {
        _isCreating = true;
        _selected = null;
        _selectedId = null;
        _draftName = "Main stream overlay";
        _draftType = _guessingFeatureEnabled ? OverlayType.Guessing : OverlayType.Empty;
        _draftShowGuessCount = true;
        _draftResultDurationSeconds = OverlayConfiguration.GuessingV1.DefaultResultDurationSeconds;
        _revealedBrowserSourceUrl = null;
        if (setFeedback)
        {
            SetSuccess(
                "New overlay ready. Name it, then select Create overlay. Nothing has been saved yet."
            );
        }
    }

    private void SelectOverlay(OverlayInstanceView overlay)
    {
        SelectOverlay(overlay, clearFeedback: true);
    }

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
        }
        _isCreating = false;
        _revealedBrowserSourceUrl = null;
        _previewKey++;
        if (clearFeedback)
        {
            _feedback = string.Empty;
            _operationFailed = false;
        }
    }

    private Task SaveAsync()
    {
        return RunOperationAsync(async () =>
        {
            if (_isCreating)
            {
                var creationResult = await _overlays.CreateAsync(
                    PageContext.Session,
                    new CreateOverlayInstanceCommand(_draftName, _draftType, DraftConfiguration()),
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
                    "This guessing overlay is paused. Turn Guessing game on in Channel setup before changing it."
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

            var configuration = DraftConfiguration();
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
    }

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

    private Task PublishTestAsync()
    {
        return RunOperationAsync(async () =>
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
            result.Match(
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
                            "No live client is connected. Select Live preview or connect the Browser Source in OBS, then try again."
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
    }

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
        _previewKey++;
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
        _previewKey++;
        SetSuccess($"{SampleLabel(sample)} sample selected.");
    }

    private OverlayConfiguration DraftConfiguration()
    {
        return _draftType switch
        {
            OverlayType.Empty => new OverlayConfiguration.EmptyV1(),
            OverlayType.Guessing => new OverlayConfiguration.GuessingV1(
                _draftShowGuessCount,
                _draftResultDurationSeconds
            ),
            _ => throw new InvalidOperationException("The selected overlay type is unsupported."),
        };
    }

    private string AbsoluteUrl(string relativeUrl)
    {
        return _navigation.ToAbsoluteUri(relativeUrl).AbsoluteUri;
    }

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

    private static string CountLabel(int count)
    {
        return count == 1 ? "1 saved overlay" : $"{count} saved overlays";
    }

    private static string UpdatedLabel(OverlayInstanceView overlay)
    {
        return $"updated {overlay.UpdatedAtUtc:yyyy-MM-dd HH:mm} UTC";
    }

    private static string TypeLabel(OverlayType type)
    {
        return type switch
        {
            OverlayType.Empty => "Empty",
            OverlayType.Guessing => "Guessing",
            _ => "Unsupported",
        };
    }

    private static string SampleLabel(GuessingOverlaySampleState sample)
    {
        return sample switch
        {
            GuessingOverlaySampleState.NoRound => "No round",
            GuessingOverlaySampleState.Open => "Open",
            GuessingOverlaySampleState.Closed => "Closed",
            GuessingOverlaySampleState.Completed => "Result",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };
    }

    private static string SampleToken(GuessingOverlaySampleState sample)
    {
        return sample switch
        {
            GuessingOverlaySampleState.NoRound => "no-round",
            GuessingOverlaySampleState.Open => "open",
            GuessingOverlaySampleState.Closed => "closed",
            GuessingOverlaySampleState.Completed => "completed",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };
    }

    private string InventoryPresenceLabel(OverlayInstanceView overlay)
    {
        return OtherConnectionCount(overlay) > 0
            ? "Diagnostic: connected"
            : "Diagnostic: no other connection";
    }

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
        }

        base.Dispose(disposing);
    }

    private string PreviewButtonClass(OverlayPreviewMode mode)
    {
        return _previewMode == mode
            ? "segmented-motion__tab segmented-motion__tab--active"
            : "segmented-motion__tab";
    }

    private enum OverlayPreviewMode
    {
        Live,
        Representative,
    }
}
