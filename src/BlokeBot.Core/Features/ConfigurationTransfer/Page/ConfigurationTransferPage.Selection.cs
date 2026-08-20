using BlokeBot.Persistence.Models;

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

    private IReadOnlyList<ImportItemResolution> ResolutionsFor(ConfigurationSectionId section) =>
        _preview
            ?.Sections.SingleOrDefault(x => x.Section == section)
            ?.Conflicts.Where(x => _resolutions.ContainsKey(ConflictKey(x)))
            .Select(x => new ImportItemResolution(
                x.ImportedId,
                _resolutions[ConflictKey(x)],
                _renames.GetValueOrDefault(ConflictKey(x)),
                x.ImportedId.StartsWith("target-", StringComparison.Ordinal)
                && int.TryParse(x.ImportedId.AsSpan(7), out var id)
                    ? id
                    : null
            ))
            .ToArray()
        ?? [];

    private async Task SetStrategyAsync(ConfigurationSectionId section, string? value)
    {
        if (!Enum.TryParse<ImportConflictStrategy>(value, out var parsed))
        {
            return;
        }

        _strategies[section] = parsed;
        await RefreshPreviewAsync();
    }

    private void SetResolution(ConfigurationImportConflict conflict, string? value)
    {
        if (Enum.TryParse<ImportConflictResolution>(value, out var parsed))
        {
            _resolutions[ConflictKey(conflict)] = parsed;
        }
    }

    private void SetRename(ConfigurationImportConflict conflict, string? value) =>
        _renames[ConflictKey(conflict)] = value?.Trim() ?? string.Empty;

    private string ResolutionValue(ConfigurationImportConflict conflict) =>
        _resolutions
            .GetValueOrDefault(ConflictKey(conflict), ImportConflictResolution.Unresolved)
            .ToString();

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

    private void ToggleEnablement(HostFeatureFlags feature, bool enabled)
    {
        if (enabled)
        {
            _ = _enablementSelections.Add(feature);
        }
        else
        {
            _ = _enablementSelections.Remove(feature);
        }
    }

    private void ToggleImport(ConfigurationSectionId section, bool enabled)
    {
        if (enabled)
        {
            _ = _importSections.Add(section);
        }
        else
        {
            _ = _importSections.Remove(section);
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
    }
}
