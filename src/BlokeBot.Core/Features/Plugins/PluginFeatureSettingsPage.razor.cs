using BlokeBot.Core.Components.Layout;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginFeatureSettingsPage
{
    private PluginConfigurationState? _configuration;
    private PluginFeatureKey? _key;
    private PluginFeatureState? _state;
    private PluginSettingsEditor? _editor;
    private PageLoadState _loadState = new PageLoadState.Loading("Loading feature settings.");
    private PageSaveFeedback? _saveFeedback;
    private bool _saving;
    private bool _sharedSettingsNeedAttention;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _stateWatch;
    private string _pluginName = string.Empty;
    private string _title = "Plugin feature";
    private string _description = string.Empty;

    [Inject]
    private PluginFeatureManager _manager { get; set; } = default!;

    [Inject]
    private IPluginFeatureSnapshotProvider _snapshots { get; set; } = default!;

    [Parameter]
    public string PluginIdValue { get; set; } = string.Empty;

    [Parameter]
    public string FeatureIdValue { get; set; } = string.Empty;

    private string _statusClass =>
        _state?.Readiness switch
        {
            PluginFeatureReadiness.Ready => "status-pill status-pill--green",
            PluginFeatureReadiness.EnabledDegraded => "status-pill status-pill--amber",
            _ => "status-pill status-pill--slate",
        };

    private string _statusLabel =>
        _state?.Readiness switch
        {
            PluginFeatureReadiness.Ready => "Ready",
            PluginFeatureReadiness.EnabledDegraded => "Needs attention",
            _ => "Disabled",
        };

    private string _statusTitle =>
        _state?.Readiness switch
        {
            PluginFeatureReadiness.Ready => $"This feature is ready for {Host?.DisplayName}.",
            PluginFeatureReadiness.EnabledDegraded =>
                "This feature is on, but setup is incomplete.",
            _ => $"This feature is off for {Host?.DisplayName}.",
        };

    private string _statusDescription =>
        _state?.Readiness switch
        {
            PluginFeatureReadiness.EnabledDegraded degraded => degraded.Reason.Detail,
            PluginFeatureReadiness.Ready => "New work can start.",
            _ => "Your settings stay saved.",
        };

    protected override void OnInitialized() => _stateWatch = WatchStateAsync();

    protected override async Task OnParametersSetAsync()
    {
        _ = await LoadPageContextAsync();
        if (
            Host is null
            || !PluginId.TryCreate(PluginIdValue, out var pluginId)
            || !PluginFeatureId.TryCreate(FeatureIdValue, out var featureId)
            || !PluginHostId.TryCreate(Host.Id, out var hostId)
        )
        {
            Failure("Choose a channel and a valid plugin feature.");
            return;
        }
        _key = new(pluginId, featureId, hostId);
        await RunSelectedHostMutationAsync(Host.Id, LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        var owner = new PluginConfigurationOwner.Feature(_key!);
        var loaded = await _manager.LoadConfigurationAsync(owner, CancellationToken.None);
        if (loaded is not PluginConfigurationLoadOutcome.Loaded available)
        {
            Failure("This plugin feature is not available.");
            return;
        }
        var feature = available.Declaration.FindFeature(_key!.FeatureId)!;
        _configuration = available.Configuration;
        _state = await _manager.LoadFeatureStateAsync(_key, CancellationToken.None);
        _editor = PluginSettingsEditor.Feature(
            available.Declaration,
            feature,
            available.Configuration
        );
        _pluginName = available.Declaration.Manifest.Name;
        _title = feature.Name;
        _description = feature.Description;
        _sharedSettingsNeedAttention = false;
        _loadState = new PageLoadState.Ready();
    }

    private Task SaveAsync() => RunAuthorizedAsync(SaveCoreAsync);

    private async Task SaveCoreAsync()
    {
        if (_editor is null || _configuration is null)
        {
            return;
        }
        if (_editor.Build() is not PluginSettingsEditorBuildOutcome.Built built)
        {
            _saveFeedback = new("Check the marked settings.", PageSaveFeedbackKind.Validation);
            return;
        }
        _saving = true;
        var outcome = await _manager.SaveConfigurationAsync(
            new(_configuration.Owner, _configuration.Revision, built.Values, built.Secrets),
            CancellationToken.None
        );
        if (outcome is PluginConfigurationSaveOutcome.Invalid invalid)
        {
            _ = _editor.ApplyServerErrors(invalid.Issues);
            _saveFeedback = new("Check the marked settings.", PageSaveFeedbackKind.Validation);
        }
        else if (outcome is PluginConfigurationSaveOutcome.Saved)
        {
            _saveFeedback = new("Settings saved.", PageSaveFeedbackKind.Success);
            await LoadCoreAsync();
        }
        else
        {
            _saveFeedback = new(
                "The settings changed. Reload and try again.",
                PageSaveFeedbackKind.Failure
            );
        }
        _saving = false;
    }

    private Task EnableAsync() =>
        RunAuthorizedAsync(async () =>
        {
            var outcome = await _manager.EnableAsync(_key!, CancellationToken.None);
            if (
                outcome is PluginFeatureEnableOutcome.Rejected
                {
                    Code: PluginFeatureEnableRejectionCode.InvalidSettings,
                } rejected
            )
            {
                _saveFeedback = ApplyEnableErrors(rejected.SettingIssues);
                return;
            }
            _saveFeedback = outcome switch
            {
                PluginFeatureEnableOutcome.Enabled => new(
                    "Feature enabled.",
                    PageSaveFeedbackKind.Success
                ),
                PluginFeatureEnableOutcome.AlreadyEnabled => new(
                    "Feature enabled.",
                    PageSaveFeedbackKind.Success
                ),
                _ => new(
                    "The feature cannot start. Check its setup.",
                    PageSaveFeedbackKind.Failure
                ),
            };
            await LoadCoreAsync();
        });

    private Task DisableAsync() =>
        RunAuthorizedAsync(async () =>
        {
            _ = await _manager.DisableAsync(_key!, CancellationToken.None);
            _saveFeedback = new("Feature disabled.", PageSaveFeedbackKind.Success);
            await LoadCoreAsync();
        });

    private Task RetryAsync() =>
        RunAuthorizedAsync(async () =>
        {
            _ = await _manager.RetryAsync(_key!, CancellationToken.None);
            await LoadCoreAsync();
        });

    private Task RunAuthorizedAsync(Func<Task> operation) =>
        _key is null
            ? Task.CompletedTask
            : RunSelectedHostMutationAsync(_key.HostId.Value, operation);

    private PageSaveFeedback ApplyEnableErrors(IReadOnlyList<PluginSettingValidationIssue> issues)
    {
        var applied = _editor?.ApplyServerErrors(issues) ?? 0;
        _sharedSettingsNeedAttention = applied < issues.Count;
        return new(
            _sharedSettingsNeedAttention
                ? "Check the shared plugin settings."
                : "Check the marked settings.",
            PageSaveFeedbackKind.Validation
        );
    }

    private async Task WatchStateAsync()
    {
        var version = _snapshots.CurrentVersion;
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                version = await _snapshots.WaitForChangeAsync(version, _stopping.Token);
                await InvokeAsync(RefreshStateAsync);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    private Task RefreshStateAsync() =>
        RunAuthorizedAsync(async () =>
        {
            var key = _key;
            if (key is null)
            {
                return;
            }
            var state = await _manager.LoadFeatureStateAsync(key, CancellationToken.None);
            if (_key == key)
            {
                _state = state;
                StateHasChanged();
            }
        });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stopping.Cancel();
            _stopping.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Failure(string message) =>
        _loadState = new PageLoadState.Failure(message, OnParametersSetAsync);
}
