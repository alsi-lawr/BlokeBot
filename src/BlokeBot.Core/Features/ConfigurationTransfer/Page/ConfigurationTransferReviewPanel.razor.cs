using System.Globalization;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

public partial class ConfigurationTransferReviewPanel
{
    [Parameter, EditorRequired]
    public required ConfigurationImportPreview Preview { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlySet<ConfigurationSectionId> SelectedSections { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyDictionary<
        ConfigurationSectionId,
        ImportConflictStrategy
    > Strategies { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyDictionary<string, ImportConflictResolution> Resolutions { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyDictionary<string, string> Renames { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyDictionary<string, int> GuessingProfileTargets { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlySet<HostFeatureFlags> EnablementSelections { get; set; }

    [Parameter]
    public bool Busy { get; set; }

    [Parameter]
    public bool AllConflictsResolved { get; set; }

    [Parameter]
    public string? ApplyIssue { get; set; }

    [Parameter]
    public EventCallback<SectionSelectionChange> SectionSelectionChanged { get; set; }

    [Parameter]
    public EventCallback<SectionStrategyChange> SectionStrategyChanged { get; set; }

    [Parameter]
    public EventCallback<ConflictResolutionChange> ConflictResolutionChanged { get; set; }

    [Parameter]
    public EventCallback<ConflictRenameChange> ConflictRenameChanged { get; set; }

    [Parameter]
    public EventCallback<GuessingProfileTargetChange> GuessingProfileTargetChanged { get; set; }

    [Parameter]
    public EventCallback<EnablementSelectionChange> EnablementSelectionChanged { get; set; }

    [Parameter]
    public EventCallback Cancel { get; set; }

    [Parameter]
    public EventCallback Apply { get; set; }

    private IEnumerable<ConfigurationSectionPreview> _reviewSections =>
        Preview.Sections.Where(static section =>
            section.Section != ConfigurationSectionId.ChannelToolEnablement
        );

    private DateTimeOffset _exportedAt => Preview.Document.ExportedAtUtc.ToLocalTime();
    private string _exportedDate => _exportedAt.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
    private string _exportedTime =>
        _exportedAt.ToString("HH:mm 'local time'", CultureInfo.CurrentCulture);

    private static string StrategyId(ConfigurationSectionId section) =>
        $"configuration-transfer-strategy-{section.ToString().ToLowerInvariant()}";

    private static string GuessingMappingId(GuessingProfileMappingPreview mapping) =>
        $"guessing-profile-map-{Uri.EscapeDataString(mapping.ImportedProfileId)}";

    private static string ConflictResolutionId(ConfigurationImportConflict conflict) =>
        $"configuration-transfer-resolution-{Uri.EscapeDataString(conflict.ImportedId)}";

    private static string ConflictRenameId(ConfigurationImportConflict conflict) =>
        $"configuration-transfer-rename-{Uri.EscapeDataString(conflict.ImportedId)}";

    private bool HasNestedContent(ConfigurationSectionPreview section) =>
        section.Issues.Count > 0
        || section.GuessingProfileMappings.Count > 0
        || section.Conflicts.Count > 0;

    private string AutomaticTargetTitle(GuessingProfileMappingPreview mapping) =>
        mapping.ExistingTargets.SingleOrDefault(x => x.TargetId == mapping.AutomaticTargetId)
            is { } target
        && !TargetMappedToAnotherProfile(mapping.ImportedProfileId, target.TargetId)
            ? $"Automatic: {target.Name} ({target.Slug})"
            : "Automatic: create a new profile";

    private string GuessingProfileTargetValue(string importedProfileId) =>
        GuessingProfileTargets.TryGetValue(importedProfileId, out var targetId)
            ? targetId.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    private bool TargetMappedToAnotherProfile(string importedProfileId, int targetId) =>
        GuessingProfileTargets.Any(x => x.Key != importedProfileId && x.Value == targetId);

    private string ResolutionValue(ConfigurationImportConflict conflict) =>
        Resolutions
            .GetValueOrDefault(
                ConfigurationTransferPresentation.ConflictKey(conflict),
                ImportConflictResolution.Unresolved
            )
            .ToString();

    private static IEnumerable<ImportConflictResolution> LocalResolutions(
        ConfigurationImportConflict conflict
    ) =>
        conflict.AllowedResolutions.Where(static resolution =>
            resolution != ImportConflictResolution.Abort
        );

    private static string EnablementPillClass(bool enabled) =>
        enabled ? "status-pill status-pill--green" : "status-pill status-pill--slate";

    private static string EnabledLabel(bool enabled) => enabled ? "On" : "Off";

    private Task ChangeSectionSelectionAsync(ConfigurationSectionId section, bool selected) =>
        SectionSelectionChanged.InvokeAsync(new(section, selected));

    private Task ChangeStrategyAsync(ConfigurationSectionId section, string? value) =>
        Enum.TryParse<ImportConflictStrategy>(value, out var strategy)
            ? SectionStrategyChanged.InvokeAsync(new(section, strategy))
            : Task.CompletedTask;

    private Task ChangeResolutionAsync(ConfigurationImportConflict conflict, string? value) =>
        Enum.TryParse<ImportConflictResolution>(value, out var resolution)
            ? ConflictResolutionChanged.InvokeAsync(new(conflict, resolution))
            : Task.CompletedTask;

    private Task ChangeRenameAsync(ConfigurationImportConflict conflict, string? value) =>
        ConflictRenameChanged.InvokeAsync(new(conflict, value?.Trim() ?? string.Empty));

    private Task ChangeProfileTargetAsync(string importedProfileId, string? value) =>
        GuessingProfileTargetChanged.InvokeAsync(
            new(
                importedProfileId,
                int.TryParse(value, CultureInfo.InvariantCulture, out var targetId)
                    ? targetId
                    : null
            )
        );

    private Task ChangeEnablementSelectionAsync(HostFeatureFlags feature, bool selected) =>
        EnablementSelectionChanged.InvokeAsync(new(feature, selected));

    public sealed record SectionSelectionChange(ConfigurationSectionId Section, bool Selected);

    public sealed record SectionStrategyChange(
        ConfigurationSectionId Section,
        ImportConflictStrategy Strategy
    );

    public sealed record ConflictResolutionChange(
        ConfigurationImportConflict Conflict,
        ImportConflictResolution Resolution
    );

    public sealed record ConflictRenameChange(
        ConfigurationImportConflict Conflict,
        string ReplacementName
    );

    public sealed record GuessingProfileTargetChange(string ImportedProfileId, int? TargetId);

    public sealed record EnablementSelectionChange(HostFeatureFlags Feature, bool Selected);
}
