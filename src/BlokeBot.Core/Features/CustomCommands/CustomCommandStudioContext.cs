using BlokeBot.Core.Components.Studio;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandStudioContext
{
    public required CustomCommandConfiguration Configuration { get; init; }
    public required CustomCommandSettingsTab ActiveTab { get; init; }
    public required Func<IReadOnlyList<StudioRailGroup>> RailGroups { get; init; }
    public required EventCallback AddMessageEntry { get; init; }
    public required Action AddCommand { get; init; }
    public required Func<CustomCommandEditor?> SelectedCommand { get; init; }
    public required Func<CustomCounterEditor?> SelectedCounter { get; init; }
    public required Func<CustomAnnouncementEditor?> SelectedAnnouncement { get; init; }
    public required Func<CustomMessageLibraryEntryEditor?> SelectedReply { get; init; }
    public required Func<string> HeaderName { get; init; }
    public required Func<string?> HeaderStats { get; init; }
    public required Func<string> EmptyInspectorMessage { get; init; }
    public required Func<bool> HasChanges { get; init; }
    public required EventCallback Save { get; init; }
    public required CustomCommandValidationBindings Validation { get; init; }
    public required Func<bool> BasicsOpen { get; init; }
    public required Action<bool> SetBasicsOpen { get; init; }
    public required Func<bool> AccessOpen { get; init; }
    public required Action<bool> SetAccessOpen { get; init; }
    public required Func<bool> ActionOpen { get; init; }
    public required Action<bool> SetActionOpen { get; init; }
    public required Func<bool> FineTuningOpen { get; init; }
    public required Action<bool> SetFineTuningOpen { get; init; }
    public required Func<CustomCommandEditor, string> BasicsSummary { get; init; }
    public required Func<CustomCommandEditor, string> CommandInvocation { get; init; }
    public required Func<CustomCommandEditor, string> CommandAccessSummary { get; init; }
    public required Func<CustomCommandActionKind, string> ActionKindLabel { get; init; }
    public required Func<CustomCommandEditor, string> CommandAdvancedSummary { get; init; }
    public required Func<
        CustomCommandEditor,
        IReadOnlyList<StudioChatLine>
    > CommandPreviewLines { get; init; }
    public required Func<
        CustomCommandEditor,
        IReadOnlyList<StudioSegmentedOption<bool>>
    > AccessOptions { get; init; }
    public required Action<CustomCommandEditor, bool> SetCommandAccess { get; init; }
    public required Action<CustomCommandEditor> ToggleModeratorAccess { get; init; }
    public required Func<CustomCommandEditor, Task> AddAllowedUser { get; init; }
    public required Action<
        CustomCommandEditor,
        CustomCommandAllowedUserEditor
    > RemoveAllowedUser { get; init; }
    public required bool AutomationsEnabled { get; init; }
    public required bool OverlaysEnabled { get; init; }
    public required OverlayCueAdmissionCatalog CueCatalog { get; init; }
    public string? CueTestOutcome { get; init; }
    public required Func<OverlayCueCustomCommandActionEditor, Task> TestCue { get; init; }
    public required Func<OverlayCueQueuePolicy, string> CueQueuePolicyLabel { get; init; }
    public required Func<OverlayCueReplyOrder, string> CueReplyOrderLabel { get; init; }
    public required Func<CustomCommandCooldownScope, string> CooldownScopeLabel { get; init; }
    public required Func<CustomCommandInvocationLimit, string> InvocationLimitLabel { get; init; }
    public required Func<int?> PendingResetAllCommandId { get; init; }
    public required Func<CustomCommandEditor, Task> ResetViewer { get; init; }
    public required Action<CustomCommandEditor> RequestResetAllViewers { get; init; }
    public required Func<CustomCommandEditor, Task> ResetAllViewers { get; init; }
    public required Action CancelResetAllViewers { get; init; }
    public required Action<CustomCommandEditor> RemoveCommand { get; init; }
    public required Action<CustomCounterEditor> RemoveCounter { get; init; }
    public required Action<CustomAnnouncementEditor> RemoveAnnouncement { get; init; }
    public required Action<CustomMessageLibraryEntryEditor> RemoveMessageEntry { get; init; }
    public required Action<CustomMessageLibraryEntryEditor> AddVariant { get; init; }
    public required Action<CustomMessageLibraryEntryEditor, int, int> MoveVariant { get; init; }
    public required Action<
        CustomMessageLibraryEntryEditor,
        CustomMessageVariantEditor
    > RemoveVariant { get; init; }
    public required Func<CustomMessageSelectionMode, string> MessageSelectionLabel { get; init; }
    public required Func<
        CustomAnnouncementDeliveryType,
        string
    > AnnouncementDeliveryTypeLabel { get; init; }
    public required Func<
        BlokeBot.Persistence.Models.TwitchAnnouncementColor,
        string
    > TwitchAnnouncementColorLabel { get; init; }
    public required Func<
        TwitchAnnouncementReadiness,
        string
    > TwitchAnnouncementCapabilityMessage { get; init; }
    public required Func<
        CustomAnnouncementScheduleKind,
        string
    > AnnouncementScheduleLabel { get; init; }
    public required Func<CustomAnnouncementEditor, bool> NativeDeliveryUnavailable { get; init; }
    public required Func<int, long> AnnouncementAdvancedOpenRequest { get; init; }
    public required Func<
        CustomAnnouncementEditor,
        RenderFragment
    > AnnouncementDeliveryDetails { get; init; }
    public required Func<DateTime?, string> FormatLastSent { get; init; }
    public required Func<
        CustomAnnouncementLatestDeliveryResult,
        string
    > LatestDeliveryResultLabel { get; init; }
    public required IReadOnlyList<TimeZoneInfo> TimeZones { get; init; }
    public required long TimeZoneSectionOpenRequest { get; init; }
    public required Func<string> SelectedTimeZoneLabel { get; init; }
    public required EventCallback<ChangeEventArgs> ChangeTimeZone { get; init; }
    public required Func<DurableAlertSeverity, string> AlertImportanceLabel { get; init; }
}

public sealed record CustomCommandValidationBindings(
    Func<CustomCommandConfigurationValidationTarget, string?> Message,
    Func<CustomCommandConfigurationValidationTarget, long> FocusRequest,
    Func<string, long> EditorFocusRequest,
    Func<
        string,
        CustomCommandConfigurationValidationTarget,
        IReadOnlyDictionary<string, object>
    > Attributes,
    Func<string, CustomCommandConfigurationValidationTarget, RenderFragment> MessageFor,
    IDictionary<string, ElementReference> Controls
);
