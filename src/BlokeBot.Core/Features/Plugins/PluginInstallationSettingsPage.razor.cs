using BlokeBot.Core.Components.Layout;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginInstallationSettingsPage
{
    private PluginConfigurationState? _configuration;
    private PluginSettingsEditor? _editor;
    private PageLoadState _loadState = new PageLoadState.Loading("Loading plugin settings.");
    private PageSaveFeedback? _saveFeedback;
    private bool _saving;
    private string _title = "Plugin settings";

    [Inject]
    private PluginFeatureManager _manager { get; set; } = default!;

    [Parameter]
    public string PluginIdValue { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loadState = new PageLoadState.Loading("Loading plugin settings.");
        if (!PluginId.TryCreate(PluginIdValue, out var pluginId))
        {
            Failure("This plugin ID is invalid.");
            return;
        }

        var owner = new PluginConfigurationOwner.Installation(pluginId);
        var loaded = await _manager.LoadConfigurationAsync(owner, CancellationToken.None);
        if (loaded is not PluginConfigurationLoadOutcome.Loaded available)
        {
            Failure("This plugin is not available.");
            return;
        }

        _configuration = available.Configuration;
        _editor = PluginSettingsEditor.Installation(available.Declaration, available.Configuration);
        _title = available.Declaration.Manifest.Name;
        _loadState = new PageLoadState.Ready();
    }

    private async Task SaveAsync()
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
        _saveFeedback = new("Saving settings.", PageSaveFeedbackKind.Saving);
        var outcome = await _manager.SaveConfigurationAsync(
            new(_configuration.Owner, _configuration.Revision, built.Values, built.Secrets),
            CancellationToken.None
        );
        switch (outcome)
        {
            case PluginConfigurationSaveOutcome.Saved:
                _saveFeedback = new("Settings saved.", PageSaveFeedbackKind.Success);
                await LoadAsync();
                break;
            case PluginConfigurationSaveOutcome.Invalid invalid:
                _ = _editor.ApplyServerErrors(invalid.Issues);
                _saveFeedback = new("Check the marked settings.", PageSaveFeedbackKind.Validation);
                break;
            case PluginConfigurationSaveOutcome.Conflict:
                _saveFeedback = new(
                    "The settings changed. Reload the page and try again.",
                    PageSaveFeedbackKind.Failure
                );
                break;
            case PluginConfigurationSaveOutcome.NotDeclared:
                Failure("This plugin is not available.");
                break;
        }
        _saving = false;
    }

    private void Failure(string message) =>
        _loadState = new PageLoadState.Failure(message, LoadAsync);
}
