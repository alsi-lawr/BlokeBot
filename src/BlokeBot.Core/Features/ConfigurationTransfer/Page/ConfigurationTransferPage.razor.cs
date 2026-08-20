using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

public partial class ConfigurationTransferPage
{
    private readonly HashSet<ConfigurationSectionId> _exportSections =
        Enum.GetValues<ConfigurationSectionId>().ToHashSet();
    private readonly Dictionary<ConfigurationSectionId, ImportConflictStrategy> _strategies = [];
    private readonly Dictionary<string, ImportConflictResolution> _resolutions = [];
    private readonly Dictionary<string, string> _renames = [];
    private readonly Dictionary<string, int> _guessingProfileTargets = [];
    private readonly HashSet<HostFeatureFlags> _enablementSelections = [];
    private readonly HashSet<ConfigurationSectionId> _importSections = [];
    private PageLoadState _loadState = new PageLoadState.Loading(
        "Loading configuration transfer..."
    );
    private TransferMode _mode;
    private string _pastedJson = string.Empty;
    private ConfigurationValidationIssue? _parseIssue;
    private string? _applyIssue;
    private ConfigurationImportPreview? _preview;
    private ConfigurationDocumentV1? _document;
    private ConfigurationImportApplied? _applied;
    private ConfigurationActivationView? _activation;
    private AuthenticatedSession _session = AuthenticatedSession.Anonymous;
    private bool _busy;
    private bool _queryApplied;

    [Parameter, SupplyParameterFromQuery(Name = "mode")]
    public string? RequestedMode { get; set; }

    [Parameter, SupplyParameterFromQuery(Name = "section")]
    public string? RequestedSection { get; set; }

    [Inject]
    private ConfigurationDocumentCodec _codec { get; set; } = default!;

    [Inject]
    private ConfigurationImportPreviewService _previewService { get; set; } = default!;

    [Inject]
    private ConfigurationTransferCoordinator _coordinator { get; set; } = default!;

    [Inject]
    private ConfigurationActivationService _activations { get; set; } = default!;

    private static readonly SectionOption[] _sectionOptions =
    [
        new(
            ConfigurationSectionId.CustomCommands,
            "Custom commands",
            "Commands, aliases, reusable replies, counters and cooldowns"
        ),
        new(
            ConfigurationSectionId.Announcements,
            "Announcements",
            "Scheduled chat messages and Twitch announcements"
        ),
        new(
            ConfigurationSectionId.Guessing,
            "Guessing game",
            "Profiles, answers, aliases and replies"
        ),
        new(
            ConfigurationSectionId.Points,
            "Points & giveaways",
            "Terminology, aliases, gambling and giveaway rules"
        ),
        new(
            ConfigurationSectionId.ChannelToolEnablement,
            "Chat Tools enablement",
            "Every independent feature switch"
        ),
    ];

    protected override async Task OnInitializedAsync()
    {
        var page = await LoadPageContextAsync();
        _session = page.Session;
        _loadState =
            HostId == 0
                ? new PageLoadState.Failure(
                    "Choose a channel before transferring configuration.",
                    OnInitializedAsync
                )
                : new PageLoadState.Ready();
    }

    protected override void OnParametersSet()
    {
        if (_queryApplied)
        {
            return;
        }

        _queryApplied = true;
        _mode = string.Equals(RequestedMode, "import", StringComparison.OrdinalIgnoreCase)
            ? TransferMode.Import
            : TransferMode.Export;
        if (ParseSection(RequestedSection) is { } section)
        {
            _exportSections.Clear();
            _ = _exportSections.Add(section);
        }
    }

    private string _destinationInitial => HostLogin.FirstOrDefault().ToString().ToUpperInvariant();
    private string _exportUrl =>
        $"/configuration-transfer/export?sections={Uri.EscapeDataString(string.Join(',', _exportSections.Order()))}";
    private bool _allConflictsResolved =>
        _importSections.Count > 0
        && _preview
            ?.Sections.Where(x => _importSections.Contains(x.Section))
            .SelectMany(x => x.Conflicts)
            .All(x =>
                _resolutions.GetValueOrDefault(ConflictKey(x)) is { } resolution
                && resolution != ImportConflictResolution.Unresolved
                && (
                    resolution != ImportConflictResolution.Rename
                    || !string.IsNullOrWhiteSpace(_renames.GetValueOrDefault(ConflictKey(x)))
                )
            ) == true;

    private static string ConflictKey(ConfigurationImportConflict x) =>
        $"{x.Section}:{x.ImportedId}";

    private static string GuessingMappingId(GuessingProfileMappingPreview mapping) =>
        $"guessing-profile-map-{Uri.EscapeDataString(mapping.ImportedProfileId)}";

    private string AutomaticTargetTitle(GuessingProfileMappingPreview mapping) =>
        mapping.ExistingTargets.SingleOrDefault(x => x.TargetId == mapping.AutomaticTargetId)
            is { } target
        && !TargetMappedToAnotherProfile(mapping.ImportedProfileId, target.TargetId)
            ? $"Automatic: {target.Name} ({target.Slug})"
            : "Automatic: create a new profile";

    private static ConfigurationSectionId? ParseSection(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "customcommands" => ConfigurationSectionId.CustomCommands,
            "announcements" => ConfigurationSectionId.Announcements,
            "guessing" => ConfigurationSectionId.Guessing,
            "points" => ConfigurationSectionId.Points,
            "channeltoolenablement" => ConfigurationSectionId.ChannelToolEnablement,
            _ => null,
        };

    private static IReadOnlyList<ConfigurationSectionId> PresentSections(
        ConfigurationDocumentV1 x
    ) =>
        [
            .. new (ConfigurationSectionId, bool)[]
            {
                (ConfigurationSectionId.CustomCommands, x.Sections.CustomCommands is not null),
                (ConfigurationSectionId.Announcements, x.Sections.Announcements is not null),
                (ConfigurationSectionId.Guessing, x.Sections.Guessing is not null),
                (ConfigurationSectionId.Points, x.Sections.Points is not null),
                (
                    ConfigurationSectionId.ChannelToolEnablement,
                    x.Sections.ChannelToolEnablement is not null
                ),
            }
                .Where(x => x.Item2)
                .Select(x => x.Item1),
        ];

    private sealed record SectionOption(
        ConfigurationSectionId Id,
        string Title,
        string Description
    );

    private enum TransferMode
    {
        Export,
        Import,
    }
}
