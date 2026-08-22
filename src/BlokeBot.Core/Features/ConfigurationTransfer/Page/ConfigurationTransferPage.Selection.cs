namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

public partial class ConfigurationTransferPage
{
    private ConfigurationImportSelection BuildSelection() =>
        new(
            HostId,
            _strategies
                .Where(x => _importSections.Contains(x.Key))
                .Select(x => new SectionImportSelection(x.Key, x.Value, ResolutionsFor(x.Key)))
                .ToArray(),
            _enablementSelections
        );

    private ConfigurationImportSelection BuildPreviewSelection() =>
        new(
            HostId,
            _strategies
                .Select(x => new SectionImportSelection(x.Key, x.Value, ResolutionsFor(x.Key)))
                .ToArray(),
            _enablementSelections
        );

    private IReadOnlyList<ImportItemResolution> ResolutionsFor(ConfigurationSectionId section)
    {
        var conflicts =
            _preview
                ?.Sections.SingleOrDefault(x => x.Section == section)
                ?.Conflicts.Where(x =>
                    _resolutions.ContainsKey(ConfigurationTransferPresentation.ConflictKey(x))
                )
                .Select(x => new ImportItemResolution(
                    x.ImportedId,
                    _resolutions[ConfigurationTransferPresentation.ConflictKey(x)],
                    _renames.GetValueOrDefault(ConfigurationTransferPresentation.ConflictKey(x)),
                    x.ImportedId.StartsWith("target-", StringComparison.Ordinal)
                    && int.TryParse(x.ImportedId.AsSpan(7), out var id)
                        ? id
                        : null
                ))
                .ToArray()
            ?? [];
        return section == ConfigurationSectionId.Guessing
            ?
            [
                .. conflicts,
                .. _guessingProfileTargets.Select(x => new ImportItemResolution(
                    x.Key,
                    ImportConflictResolution.Replace,
                    TargetId: x.Value
                )),
            ]
            : conflicts;
    }

    private async Task SetStrategyAsync(
        ConfigurationTransferReviewPanel.SectionStrategyChange change
    )
    {
        _strategies[change.Section] = change.Strategy;
        await RefreshPreviewAsync();
    }

    private async Task SetResolutionAsync(
        ConfigurationTransferReviewPanel.ConflictResolutionChange change
    )
    {
        _resolutions[ConfigurationTransferPresentation.ConflictKey(change.Conflict)] =
            change.Resolution;
        await RefreshPreviewAsync();
    }

    private async Task SetRenameAsync(ConfigurationTransferReviewPanel.ConflictRenameChange change)
    {
        _renames[ConfigurationTransferPresentation.ConflictKey(change.Conflict)] =
            change.ReplacementName;
        await RefreshPreviewAsync();
    }

    private async Task SetGuessingProfileTargetAsync(
        ConfigurationTransferReviewPanel.GuessingProfileTargetChange change
    )
    {
        if (change.TargetId is { } targetId)
        {
            _guessingProfileTargets[change.ImportedProfileId] = targetId;
        }
        else
        {
            _ = _guessingProfileTargets.Remove(change.ImportedProfileId);
        }
        await RefreshPreviewAsync();
    }

    private void ToggleExport(ConfigurationSectionId section, bool enabled)
    {
        if (enabled)
        {
            _ = _exportSections.Add(section);
        }
        else
        {
            _ = _exportSections.Remove(section);
        }
    }

    private void ToggleOverlayUrls(bool enabled)
    {
        _exportOverlayUrls = enabled;
        if (!enabled)
        {
            _urlWarningAcknowledged = false;
        }
    }

    private void ToggleOverlayMedia(bool enabled) => _exportOverlayMedia = enabled;

    private void ToggleUrlWarning(bool enabled) => _urlWarningAcknowledged = enabled;

    private void ToggleEnablement(ConfigurationTransferReviewPanel.EnablementSelectionChange change)
    {
        if (change.Selected)
        {
            _ = _enablementSelections.Add(change.Feature);
        }
        else
        {
            _ = _enablementSelections.Remove(change.Feature);
        }
    }

    private void ToggleImport(ConfigurationTransferReviewPanel.SectionSelectionChange change)
    {
        if (change.Selected)
        {
            _ = _importSections.Add(change.Section);
        }
        else
        {
            _ = _importSections.Remove(change.Section);
        }
    }

    private void ResetImport()
    {
        _activationPoll?.Cancel();
        _preview = null;
        _document = null;
        _applied = null;
        _activation = null;
        _parseIssue = null;
        _applyIssue = null;
        _pastedJson = string.Empty;
        _importSections.Clear();
        _guessingProfileTargets.Clear();
    }

    private void CancelImport() => ResetImport();
}
