using System.Globalization;
using System.Text;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CustomCommands;

public partial class CustomCommandSettingsPage
{
    private enum CustomCommandEditorKind
    {
        Reply,
        Command,
        Counter,
        ScheduledMessage,
    }

    private sealed record CustomCommandEditorSelection(CustomCommandEditorKind Kind, int Id);

    private static readonly IReadOnlyList<SegmentedTabItem> _settingsTabs =
    [
        new("commands", "Commands"),
        new("message-library", "Message Library"),
    ];

    private static readonly IReadOnlyList<CustomMessageSelectionMode> _messageSelectionModes =
        Enum.GetValues<CustomMessageSelectionMode>();
    private static readonly IReadOnlyList<CustomCommandCooldownScope> _cooldownScopes =
        Enum.GetValues<CustomCommandCooldownScope>();
    private static readonly IReadOnlyList<CustomCommandInvocationLimit> _invocationLimits =
        Enum.GetValues<CustomCommandInvocationLimit>();
    private static readonly IReadOnlyList<CustomCommandActionKind> _actionKinds =
    [
        CustomCommandActionKind.Message,
        CustomCommandActionKind.Counter,
        CustomCommandActionKind.OverlayCue,
    ];
    private static readonly IReadOnlyList<OverlayCueQueuePolicy> _cueQueuePolicies =
        Enum.GetValues<OverlayCueQueuePolicy>();
    private static readonly IReadOnlyList<OverlayCueReplyOrder> _cueReplyOrders =
        Enum.GetValues<OverlayCueReplyOrder>();
    private static readonly IReadOnlyList<CustomAnnouncementScheduleKind> _announcementScheduleKinds =
        Enum.GetValues<CustomAnnouncementScheduleKind>();
    private static readonly IReadOnlyList<CustomAnnouncementDeliveryType> _announcementDeliveryTypes =
        Enum.GetValues<CustomAnnouncementDeliveryType>();
    private static readonly IReadOnlyList<BlokeBot.Persistence.Models.TwitchAnnouncementColor> _twitchAnnouncementColors =
        Enum.GetValues<BlokeBot.Persistence.Models.TwitchAnnouncementColor>();
    private static readonly IReadOnlyList<DayOfWeek> _daysOfWeek = Enum.GetValues<DayOfWeek>();
    private static readonly IReadOnlyList<TimeZoneInfo> _timeZones =
        TimeZoneInfo.GetSystemTimeZones();

    private CustomCommandConfiguration? _config;
    private string? _loadedConfigurationFingerprint;
    private string? _loadFailureMessage;
    private bool _featureEnabled;
    private bool _overlaysEnabled;
    private OverlayCueAdmissionCatalog _cueCatalog = new([], []);
    private string? _cueTestOutcome;
    private bool _isLoading = true;
    private int _nextTemporaryId = -1;
    private IReadOnlyList<CustomCommandConfigurationValidationError> _validationErrors = [];
    private CustomCommandSettingsTab _activeTab;
    private CustomCommandConfigurationValidationTarget? _focusTarget;
    private long _fieldFocusRequest;
    private string? _pendingControlFocusId;
    private string? _editorFocusControlId;
    private long _replySectionOpenRequest;
    private long _commandSectionOpenRequest;
    private long _commandAdvancedOpenRequest;
    private int? _commandAdvancedEntityId;
    private long _commandAccessOpenRequest;
    private int? _commandAccessEntityId;
    private long _counterSectionOpenRequest;
    private long _announcementSectionOpenRequest;
    private long _announcementAdvancedOpenRequest;
    private int? _announcementAdvancedEntityId;
    private long _timeZoneSectionOpenRequest;
    private readonly Dictionary<string, ElementReference> _controls = [];
    private int? _pendingResetAllCommandId;
    private CustomCommandEditorSelection? _selectedEditor;

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.CustomCommandsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        _loadFailureMessage = null;
        try
        {
            await LoadCoreAsync();
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(LoadAsync), exception);
            _config = null;
            _loadedConfigurationFingerprint = null;
            _loadFailureMessage =
                "BlokeBot could not load these settings. Check the connection and try again.";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadCoreAsync()
    {
        _ = await LoadPageContextAsync();
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.CustomCommands,
                CancellationToken.None
            );
        _overlaysEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Overlays,
                CancellationToken.None
            );
        _cueCatalog =
            _featureEnabled && _overlaysEnabled
                ? await _overlayCues.QueryCatalogAsync(HostId, CancellationToken.None)
                : new([], []);
        _config = _featureEnabled
            ? await _configuration.LoadConfigurationAsync(HostId, CancellationToken.None)
            : null;
        _nextTemporaryId = -1;
        _validationErrors = [];
        _activeTab = TabForKey(SegmentedTabs.CanonicalKey(_navigation, _settingsTabs));
        _focusTarget = null;
        _pendingResetAllCommandId = null;
        _cueTestOutcome = null;
        _commandAdvancedEntityId = null;
        _commandAdvancedOpenRequest = 0;
        _commandAccessEntityId = null;
        _commandAccessOpenRequest = 0;
        _announcementAdvancedEntityId = null;
        _announcementAdvancedOpenRequest = 0;
        EnsureEditorSelection();
        _loadedConfigurationFingerprint = _config is null
            ? null
            : EditableConfigurationFingerprint(_config);
    }

    private Task SaveAsync() => ObserveUiOperationAsync(nameof(SaveAsync), SaveCoreAsync);

    private async Task SaveCoreAsync()
    {
        if (_config is null || HostId == 0)
        {
            return;
        }

        await CustomCommandConfigurationValidator
            .Validate(_config)
            .Match(
                command =>
                {
                    _validationErrors = [];
                    return SaveCommandAsync(command);
                },
                errors =>
                {
                    _validationErrors = errors.ToArray();
                    if (_validationErrors.FirstOrDefault() is { } error)
                    {
                        FocusValidationTarget(error.Target);
                    }
                    _ = _toasts.Publish(
                        new ToastRequest<ErrorToastStrategy>("Custom commands need attention.")
                    );
                    return Task.CompletedTask;
                }
            );
    }

    private async Task SaveCommandAsync(CustomCommandConfigurationSaveCommand command)
    {
        try
        {
            await RunSelectedHostMutationAsync(
                HostId,
                async () =>
                {
                    var result = await _configuration
                        .SaveConfiguration(HostId, command)
                        .ExecuteAsync(CancellationToken.None);
                    await result.Match(
                        async completed =>
                        {
                            _config = await _configuration.LoadConfigurationAsync(
                                HostId,
                                CancellationToken.None
                            );
                            _nextTemporaryId = -1;
                            _validationErrors = [];
                            _focusTarget = null;
                            _commandAdvancedEntityId = null;
                            _commandAdvancedOpenRequest = 0;
                            _commandAccessEntityId = null;
                            _commandAccessOpenRequest = 0;
                            _announcementAdvancedEntityId = null;
                            _announcementAdvancedOpenRequest = 0;
                            EnsureEditorSelection();
                            _loadedConfigurationFingerprint = EditableConfigurationFingerprint(
                                _config
                            );
                            _ = _toasts.Publish(
                                new ToastRequest<SuccessToastStrategy>("Custom commands saved.")
                            );
                        },
                        failure =>
                        {
                            _ = _toasts.Publish(
                                new ToastRequest<ErrorToastStrategy>(failure.Message)
                            );
                            SetAccessSaveFailure(failure.Message);
                            if (AliasCollisionTarget(failure) is { } target)
                            {
                                _validationErrors = [new(failure.Message, target)];
                                FocusValidationTarget(target);
                            }

                            return Task.CompletedTask;
                        }
                    );
                }
            );
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(SaveAsync), exception);
            SetAccessSaveFailure("Try again without reloading the page.");
            _ = _toasts.Publish(
                new ToastRequest<ErrorToastStrategy>("Custom commands could not be saved.")
            );
        }
    }

    private void SetAccessSaveFailure(string message)
    {
        if (_config is null)
        {
            return;
        }

        foreach (var command in _config.Commands)
        {
            command.AllowedUserFeedback = $"Changes were not saved. {message}";
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (
            _pendingControlFocusId is { } controlId
            && _controls.TryGetValue(controlId, out var control)
        )
        {
            _pendingControlFocusId = null;
            await control.FocusAsync();
        }
    }

    private static CustomCommandSettingsTab TabForKey(string key) =>
        key == "message-library"
            ? CustomCommandSettingsTab.MessageLibrary
            : CustomCommandSettingsTab.Commands;

    private static string KeyForTab(CustomCommandSettingsTab tab) =>
        tab == CustomCommandSettingsTab.MessageLibrary ? "message-library" : "commands";

    private void HandleTabKeyChanged(string key)
    {
        _activeTab = TabForKey(key);
        EnsureEditorSelection();
    }

    private void SwitchTab(CustomCommandSettingsTab tab)
    {
        if (_activeTab == tab)
        {
            return;
        }

        _activeTab = tab;
        var uri = _navigation.ToAbsoluteUri(_navigation.Uri);
        _navigation.NavigateTo(uri.GetLeftPart(UriPartial.Query) + "#" + KeyForTab(tab));
    }

    private void SelectValidationEditor(CustomCommandConfigurationValidationTarget target)
    {
        var selection = target.EntityKind switch
        {
            CustomCommandValidationEntityKind.Reply or CustomCommandValidationEntityKind.Variant =>
                new CustomCommandEditorSelection(CustomCommandEditorKind.Reply, target.EntityId),
            CustomCommandValidationEntityKind.Command => new(
                CustomCommandEditorKind.Command,
                target.EntityId
            ),
            CustomCommandValidationEntityKind.Counter => new(
                CustomCommandEditorKind.Counter,
                target.EntityId
            ),
            CustomCommandValidationEntityKind.ScheduledMessage => new(
                CustomCommandEditorKind.ScheduledMessage,
                target.EntityId
            ),
            _ => null,
        };
        if (selection is not null)
        {
            _selectedEditor = selection;
        }
    }

    private bool IsEditorOpen(CustomCommandEditorKind kind, int id) =>
        _selectedEditor is { Kind: var selectedKind, Id: var selectedId }
        && selectedKind == kind
        && selectedId == id;

    private void SelectEditor(CustomCommandEditorKind kind, int id, string focusControlId)
    {
        _selectedEditor = new(kind, id);
        _editorFocusControlId = focusControlId;
        _fieldFocusRequest++;
    }

    private void EnsureEditorSelection()
    {
        if (
            _config is null
            || (
                _selectedEditor is { } selection
                && EditorExists(selection)
                && SelectionBelongsToActiveTab(selection)
            )
        )
        {
            return;
        }

        _selectedEditor = _activeTab switch
        {
            CustomCommandSettingsTab.MessageLibrary => _config.MessageEntries.FirstOrDefault()
                is { } reply
                ? new(CustomCommandEditorKind.Reply, reply.Id)
                : null,
            CustomCommandSettingsTab.Commands => _config.Commands.FirstOrDefault() is { } command
                ? new(CustomCommandEditorKind.Command, command.Id)
                : FirstCommandTabSelection(),
            _ => null,
        };
    }

    private bool SelectionBelongsToActiveTab(CustomCommandEditorSelection selection) =>
        _activeTab switch
        {
            CustomCommandSettingsTab.MessageLibrary => selection.Kind
                == CustomCommandEditorKind.Reply,
            CustomCommandSettingsTab.Commands => selection.Kind
                is CustomCommandEditorKind.Command
                    or CustomCommandEditorKind.Counter
                    or CustomCommandEditorKind.ScheduledMessage,
            _ => false,
        };

    private bool EditorExists(CustomCommandEditorSelection selection) =>
        _config is not null
        && selection.Kind switch
        {
            CustomCommandEditorKind.Reply => _config.MessageEntries.Any(x => x.Id == selection.Id),
            CustomCommandEditorKind.Command => _config.Commands.Any(x => x.Id == selection.Id),
            CustomCommandEditorKind.Counter => _config.Counters.Any(x => x.Id == selection.Id),
            CustomCommandEditorKind.ScheduledMessage => _config.Announcements.Any(x =>
                x.Id == selection.Id
            ),
            _ => false,
        };

    private long EditorFocusRequestFor(string controlId) =>
        _editorFocusControlId == controlId ? _fieldFocusRequest : 0;

    private static string InventoryLabelId(CustomCommandEditorKind kind, int id) =>
        $"custom-command-{kind.ToString().ToLowerInvariant()}-{id}-inventory-label";

    private static string EditorRegionId(CustomCommandEditorKind kind, int id) =>
        $"custom-command-{kind.ToString().ToLowerInvariant()}-{id}-editor";

    private long CommandAdvancedOpenRequest(int commandId) =>
        _commandAdvancedEntityId == commandId ? _commandAdvancedOpenRequest : 0;

    private long CommandAccessOpenRequest(int commandId) =>
        _commandAccessEntityId == commandId ? _commandAccessOpenRequest : 0;

    private long AnnouncementAdvancedOpenRequest(int announcementId) =>
        _announcementAdvancedEntityId == announcementId ? _announcementAdvancedOpenRequest : 0;

    private bool _hasChanges =>
        _config is not null
        && _loadedConfigurationFingerprint is not null
        && EditableConfigurationFingerprint(_config) != _loadedConfigurationFingerprint;

    private static string EditableConfigurationFingerprint(CustomCommandConfiguration config)
    {
        var result = new StringBuilder();

        static void Append(StringBuilder builder, object? value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            _ = builder.Append(text.Length);
            _ = builder.Append(':');
            _ = builder.Append(text);
            _ = builder.Append('|');
        }

        Append(result, config.TimeZoneId);
        foreach (var entry in config.MessageEntries)
        {
            Append(result, "reply");
            Append(result, entry.Id);
            Append(result, entry.Name);
            Append(result, entry.SelectionMode);
            Append(result, entry.CurrentVariantIndex);
            foreach (var variant in entry.Variants)
            {
                Append(result, variant.Id);
                Append(result, variant.Text);
            }
        }

        foreach (var command in config.Commands)
        {
            Append(result, "command");
            Append(result, command.Id);
            Append(result, command.Name);
            Append(result, command.Aliases);
            Append(result, command.Enabled);
            Append(result, command.AllowEveryone);
            Append(result, command.AllowModerators);
            foreach (var user in command.AllowedUsers)
            {
                Append(result, user.TwitchUserId);
                Append(result, user.Login);
                Append(result, user.DisplayName);
            }
            Append(result, command.CooldownSeconds);
            Append(result, command.CooldownScope);
            Append(result, command.InvocationLimit);
            Append(result, command.ActionKind);
            Append(result, command.Action.ReplyRoutes.ZeroArgumentMessageLibraryEntryId);
            Append(result, command.Action.ReplyRoutes.OneArgumentMessageLibraryEntryId);
            Append(result, command.Action.ReplyRoutes.TwoArgumentMessageLibraryEntryId);
            if (command.Action is CounterCustomCommandActionEditor counterAction)
            {
                Append(result, counterAction.CounterId);
            }
            else if (command.Action is OverlayCueCustomCommandActionEditor cueAction)
            {
                Append(result, cueAction.TargetOverlayPublicId);
                Append(result, cueAction.CuePublicId);
                Append(result, cueAction.QueuePolicy);
                Append(result, cueAction.ReplyOrder);
            }
        }

        foreach (var counter in config.Counters)
        {
            Append(result, "counter");
            Append(result, counter.Id);
            Append(result, counter.Name);
            Append(result, counter.Value);
        }

        foreach (var announcement in config.Announcements)
        {
            Append(result, "announcement");
            Append(result, announcement.Id);
            Append(result, announcement.Name);
            Append(result, announcement.Enabled);
            Append(result, announcement.MessageLibraryEntryId);
            Append(result, announcement.DeliveryType);
            Append(result, announcement.AnnouncementColor);
            Append(result, announcement.RetryDelaySeconds);
            Append(result, announcement.OccurrenceLifetimeSeconds);
            Append(result, announcement.ScheduleKind);
            switch (announcement.Schedule)
            {
                case IntervalCustomAnnouncementScheduleEditor interval:
                    Append(result, interval.IntervalMinutes);
                    break;
                case IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat:
                    Append(result, intervalAfterChat.IntervalMinutes);
                    Append(result, intervalAfterChat.RequiredChatMessages);
                    break;
                case WeeklyCustomAnnouncementScheduleEditor weekly:
                    Append(result, weekly.Day);
                    Append(result, weekly.Time);
                    break;
            }
        }

        return result.ToString();
    }

    private void FocusValidationTarget(CustomCommandConfigurationValidationTarget target)
    {
        SwitchTab(target.Tab);
        SelectValidationEditor(target);
        OpenValidationSection(target);
        _focusTarget = target;
        _fieldFocusRequest++;
        _pendingControlFocusId = ValidationControlId(target);
    }

    private void OpenValidationSection(CustomCommandConfigurationValidationTarget target)
    {
        switch (target)
        {
            case {
                Tab: CustomCommandSettingsTab.MessageLibrary,
                EntityKind: CustomCommandValidationEntityKind.Reply
                    or CustomCommandValidationEntityKind.Variant,
            }:
                _replySectionOpenRequest++;
                break;
            case {
                Tab: CustomCommandSettingsTab.Commands,
                EntityKind: CustomCommandValidationEntityKind.Command,
            }:
                _commandSectionOpenRequest++;
                if (
                    target.FieldKind
                    is CustomCommandValidationFieldKind.Cooldown
                        or CustomCommandValidationFieldKind.CooldownScope
                        or CustomCommandValidationFieldKind.InvocationLimit
                        or CustomCommandValidationFieldKind.Counter
                )
                {
                    _commandAdvancedEntityId = target.EntityId;
                    _commandAdvancedOpenRequest++;
                }
                if (target.FieldKind == CustomCommandValidationFieldKind.AllowedUsers)
                {
                    _commandAccessEntityId = target.EntityId;
                    _commandAccessOpenRequest++;
                }
                break;
            case {
                Tab: CustomCommandSettingsTab.Commands,
                EntityKind: CustomCommandValidationEntityKind.Counter,
            }:
                _counterSectionOpenRequest++;
                break;
            case {
                Tab: CustomCommandSettingsTab.Commands,
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
            }:
                _announcementSectionOpenRequest++;
                if (
                    target.FieldKind
                    is CustomCommandValidationFieldKind.RetryDelay
                        or CustomCommandValidationFieldKind.OccurrenceLifetime
                )
                {
                    _announcementAdvancedEntityId = target.EntityId;
                    _announcementAdvancedOpenRequest++;
                }
                break;
            case {
                Tab: CustomCommandSettingsTab.Commands,
                EntityKind: CustomCommandValidationEntityKind.Configuration,
                FieldKind: CustomCommandValidationFieldKind.TimeZone,
            }:
                _timeZoneSectionOpenRequest++;
                break;
        }
    }

    private CustomCommandConfigurationValidationTarget? AliasCollisionTarget(
        CustomCommandConfigurationSaveFailure failure
    ) =>
        failure.Match<CustomCommandConfigurationValidationTarget?>(
            builtInAliasCollision => CommandAliasesTarget(builtInAliasCollision.Alias),
            customAliasCollision => CommandAliasesTarget(customAliasCollision.Alias),
            _ => null,
            cueReference => CommandTarget(cueReference.CommandId, cueReference.Field)
        );

    private CustomCommandConfigurationValidationTarget? CommandAliasesTarget(string alias)
    {
        var command = _config?.Commands.FirstOrDefault(command =>
            CommandAliasNormalizer
                .Split(command.Aliases)
                .Contains(alias, StringComparer.OrdinalIgnoreCase)
        );
        return command is null
            ? null
            : new(
                CustomCommandSettingsTab.Commands,
                CustomCommandValidationEntityKind.Command,
                command.Id,
                CustomCommandValidationFieldKind.Aliases
            );
    }

    private static CustomCommandConfigurationValidationTarget ReplyTarget(
        int replyId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.MessageLibrary,
            CustomCommandValidationEntityKind.Reply,
            replyId,
            field
        );

    private static CustomCommandConfigurationValidationTarget VariantTarget(
        int replyId,
        int variantId
    ) =>
        new(
            CustomCommandSettingsTab.MessageLibrary,
            CustomCommandValidationEntityKind.Variant,
            replyId,
            CustomCommandValidationFieldKind.VariantText,
            variantId
        );

    private static CustomCommandConfigurationValidationTarget CommandTarget(
        int commandId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Command,
            commandId,
            field
        );

    private static CustomCommandConfigurationValidationTarget CounterTarget(
        int counterId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Counter,
            counterId,
            field
        );

    private static CustomCommandConfigurationValidationTarget AnnouncementTarget(
        int announcementId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.ScheduledMessage,
            announcementId,
            field
        );

    private static CustomCommandConfigurationValidationTarget ConfigurationTarget(
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Configuration,
            0,
            field
        );

    private static string MessageVariantFieldId(
        CustomMessageLibraryEntryEditor entry,
        CustomMessageVariantEditor variant
    ) => $"message-entry-{entry.Id}-variant-{variant.Id}";

    private static string CommandAliasesFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-aliases";

    private static string MessageEntryNameFieldId(CustomMessageLibraryEntryEditor entry) =>
        $"message-entry-{entry.Id}-name";

    private static string MessageSelectionFieldId(CustomMessageLibraryEntryEditor entry) =>
        $"message-entry-{entry.Id}-selection-mode";

    private static string MessageCurrentVariantFieldId(CustomMessageLibraryEntryEditor entry) =>
        $"message-entry-{entry.Id}-current-variant";

    private static string AddMessageVariantControlId(CustomMessageLibraryEntryEditor entry) =>
        $"message-entry-{entry.Id}-add-variant";

    private static string CommandNameFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-name";

    private static string CommandCooldownFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-cooldown";

    private static string CommandEnabledToggleId(CustomCommandEditor command) =>
        $"command-{command.Id}-enabled";

    private static string CommandAccessLabelId(CustomCommandEditor command) =>
        $"command-{command.Id}-access-label";

    private static string CommandEveryoneAccessId(CustomCommandEditor command) =>
        $"command-{command.Id}-access-everyone";

    private static string CommandRestrictedAccessId(CustomCommandEditor command) =>
        $"command-{command.Id}-access-restricted";

    private static string CommandModeratorAccessId(CustomCommandEditor command) =>
        $"command-{command.Id}-access-moderators";

    private static string CommandSelectedUsersContentId(CustomCommandEditor command) =>
        $"command-{command.Id}-selected-users";

    private static string CommandAllowedUserFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-allowed-user";

    private static string CommandCooldownScopeFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-cooldown-scope";

    private static string CommandInvocationLimitFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-invocation-limit";

    private static string CommandResetViewerFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-reset-viewer";

    private static string CommandActionFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-action-kind";

    private static string CommandReplyFieldId(CustomCommandEditor command, int argumentCount) =>
        $"command-{command.Id}-{argumentCount}-argument-reply";

    private static string CommandCounterFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-counter-id";

    private static string CommandOverlayTargetFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-overlay-target";

    private static string CommandOverlayCueFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-overlay-cue";

    private static string CommandQueuePolicyFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-queue-policy";

    private static string CommandReplyOrderFieldId(CustomCommandEditor command) =>
        $"command-{command.Id}-reply-order";

    private static string CounterNameFieldId(CustomCounterEditor counter) =>
        $"counter-{counter.Id}-name";

    private static string CounterValueFieldId(CustomCounterEditor counter) =>
        $"counter-{counter.Id}-value";

    private static string AnnouncementNameFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-name";

    private static string AnnouncementReplyFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-reply";

    private static string AnnouncementEnabledToggleId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-enabled";

    private static string AnnouncementDeliveryFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-delivery";

    private static string AnnouncementColorFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-color";

    private static string AnnouncementScheduleFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-schedule-kind";

    private static string AnnouncementRetryDelayFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-retry-delay";

    private static string AnnouncementOccurrenceLifetimeFieldId(
        CustomAnnouncementEditor announcement
    ) => $"announcement-{announcement.Id}-occurrence-lifetime";

    private static string AnnouncementIntervalFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-interval-minutes";

    private static string AnnouncementChatMessagesFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-required-chat-messages";

    private static string AnnouncementDayFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-day";

    private static string AnnouncementWeeklyTimeFieldId(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-weekly-time";

    private const string _timeZoneControlId = "custom-command-time-zone";
    private const string _reloadControlId = "custom-command-reload";

    private string? ValidationMessage(CustomCommandConfigurationValidationTarget target) =>
        _validationErrors.FirstOrDefault(error => error.Target == target)?.Message;

    private long FocusRequestFor(CustomCommandConfigurationValidationTarget target) =>
        _focusTarget == target ? _fieldFocusRequest : 0;

    private IReadOnlyDictionary<string, object> ValidationAttributes(
        string controlId,
        CustomCommandConfigurationValidationTarget target
    ) =>
        ValidationMessage(target) is null
            ? []
            : new Dictionary<string, object>
            {
                ["aria-invalid"] = "true",
                ["aria-describedby"] = $"{controlId}-error",
            };

    private RenderFragment ValidationMessageFor(
        string controlId,
        CustomCommandConfigurationValidationTarget target
    )
    {
        var message = ValidationMessage(target);
        return builder =>
        {
            if (message is null)
            {
                return;
            }

            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "id", $"{controlId}-error");
            builder.AddAttribute(2, "class", "text-sm font-semibold text-red-700");
            builder.AddAttribute(3, "role", "alert");
            builder.AddContent(4, message);
            builder.CloseElement();
        };
    }

    private static string? ValidationControlId(CustomCommandConfigurationValidationTarget target) =>
        target switch
        {
            {
                EntityKind: CustomCommandValidationEntityKind.Configuration,
                FieldKind: CustomCommandValidationFieldKind.Identity
            } => _reloadControlId,
            {
                EntityKind: CustomCommandValidationEntityKind.Configuration,
                FieldKind: CustomCommandValidationFieldKind.TimeZone
            } => _timeZoneControlId,
            {
                EntityKind: CustomCommandValidationEntityKind.Reply,
                FieldKind: CustomCommandValidationFieldKind.SelectionMode
            } => $"message-entry-{target.EntityId}-selection-mode",
            {
                EntityKind: CustomCommandValidationEntityKind.Reply,
                FieldKind: CustomCommandValidationFieldKind.VariantText
            } => $"message-entry-{target.EntityId}-add-variant",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.AllowedUsers
            } => $"command-{target.EntityId}-allowed-user",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.Cooldown
            } => $"command-{target.EntityId}-cooldown",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.CooldownScope
            } => $"command-{target.EntityId}-cooldown-scope",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.InvocationLimit
            } => $"command-{target.EntityId}-invocation-limit",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.Action
            } => $"command-{target.EntityId}-action-kind",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.ZeroArgumentReply
            } => $"command-{target.EntityId}-0-argument-reply",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.OneArgumentReply
            } => $"command-{target.EntityId}-1-argument-reply",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.TwoArgumentReply
            } => $"command-{target.EntityId}-2-argument-reply",
            {
                EntityKind: CustomCommandValidationEntityKind.Command,
                FieldKind: CustomCommandValidationFieldKind.Counter
            } => $"command-{target.EntityId}-counter-id",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.Reply
            } => $"announcement-{target.EntityId}-reply",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.Delivery
            } => $"announcement-{target.EntityId}-delivery",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.Color
            } => $"announcement-{target.EntityId}-color",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.RetryDelay
            } => $"announcement-{target.EntityId}-retry-delay",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.OccurrenceLifetime
            } => $"announcement-{target.EntityId}-occurrence-lifetime",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.Schedule
            } => $"announcement-{target.EntityId}-schedule-kind",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.Interval
            } => $"announcement-{target.EntityId}-interval-minutes",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.ChatMessages
            } => $"announcement-{target.EntityId}-required-chat-messages",
            {
                EntityKind: CustomCommandValidationEntityKind.ScheduledMessage,
                FieldKind: CustomCommandValidationFieldKind.Day
            } => $"announcement-{target.EntityId}-day",
            _ => null,
        };

    private void AddMessageEntry()
    {
        if (_config is null)
        {
            return;
        }

        var entry = new CustomMessageLibraryEntryEditor
        {
            Id = NextTemporaryId(),
            Name = "New reply",
            Variants =
            [
                new CustomMessageVariantEditor
                {
                    Id = NextTemporaryId(),
                    Text = "Type your reply here.",
                },
            ],
        };
        _config.MessageEntries.Add(entry);
        SwitchTab(CustomCommandSettingsTab.MessageLibrary);
        SelectEditor(CustomCommandEditorKind.Reply, entry.Id, MessageEntryNameFieldId(entry));
    }

    private void RemoveMessageEntry(CustomMessageLibraryEntryEditor entry)
    {
        if (_config is null)
        {
            return;
        }

        if (
            _config.Commands.Any(x => CommandUsesReply(x, entry.Id))
            || _config.Announcements.Any(x => x.MessageLibraryEntryId == entry.Id)
        )
        {
            _ = _toasts.Publish(
                new ToastRequest<WarningToastStrategy>(
                    "This reply is used by a command or announcement. Change that first, then delete it."
                )
            );
            return;
        }

        _ = _config.MessageEntries.Remove(entry);
        EnsureEditorSelection();
    }

    private static bool CommandUsesReply(CustomCommandEditor command, int messageEntryId)
    {
        var routes = command.Action.ReplyRoutes;
        return routes.ZeroArgumentMessageLibraryEntryId == messageEntryId
            || routes.OneArgumentMessageLibraryEntryId == messageEntryId
            || routes.TwoArgumentMessageLibraryEntryId == messageEntryId;
    }

    private void AddVariant(CustomMessageLibraryEntryEditor entry) =>
        entry.Variants.Add(
            new CustomMessageVariantEditor
            {
                Id = NextTemporaryId(),
                Text = "Type your reply here.",
            }
        );

    private void RemoveVariant(
        CustomMessageLibraryEntryEditor entry,
        CustomMessageVariantEditor variant
    )
    {
        if (entry.Variants.Count <= 1)
        {
            _ = _toasts.Publish(
                new ToastRequest<WarningToastStrategy>("A reply needs at least one message.")
            );
            return;
        }

        _ = entry.Variants.Remove(variant);
        entry.CurrentVariantIndex = Math.Min(entry.CurrentVariantIndex, entry.Variants.Count - 1);
    }

    private static void MoveVariant(CustomMessageLibraryEntryEditor entry, int index, int direction)
    {
        var nextIndex = index + direction;
        if (nextIndex < 0 || nextIndex >= entry.Variants.Count)
        {
            return;
        }

        (entry.Variants[index], entry.Variants[nextIndex]) = (
            entry.Variants[nextIndex],
            entry.Variants[index]
        );
        entry.CurrentVariantIndex = Math.Min(entry.CurrentVariantIndex, entry.Variants.Count - 1);
    }

    private void AddCommand()
    {
        if (_config is null || _config.MessageEntries.Count == 0)
        {
            return;
        }

        var command = new CustomCommandEditor
        {
            Id = NextTemporaryId(),
            Name = "New command",
            Aliases = "newcommand",
            Action = new MessageCustomCommandActionEditor
            {
                ReplyRoutes = new CustomCommandReplyRoutesEditor
                {
                    ZeroArgumentMessageLibraryEntryId = _config.MessageEntries[0].Id,
                },
            },
        };
        _config.Commands.Add(command);
        SwitchTab(CustomCommandSettingsTab.Commands);
        SelectEditor(CustomCommandEditorKind.Command, command.Id, CommandNameFieldId(command));
    }

    private void RemoveCommand(CustomCommandEditor command)
    {
        _ = (_config?.Commands.Remove(command));
        EnsureEditorSelection();
    }

    private static void SetCommandAccess(CustomCommandEditor command, bool allowEveryone)
    {
        command.AllowEveryone = allowEveryone;
        command.AllowedUserFeedback = null;
    }

    private static void ToggleModeratorAccess(CustomCommandEditor command)
    {
        command.AllowModerators = !command.AllowModerators;
        command.AllowedUserFeedback = null;
    }

    private async Task AddAllowedUserAsync(CustomCommandEditor command)
    {
        var normalizedLogin = Login.Normalize(command.AllowedUserLoginDraft);
        if (!IsValidTwitchLogin(normalizedLogin))
        {
            command.AllowedUserFeedback = "Enter a valid Twitch login.";
            return;
        }

        command.AllowedUserLookupInProgress = true;
        command.AllowedUserFeedback = "Looking up that Twitch account...";
        try
        {
            var resolution = await _viewers.ResolveAsync(normalizedLogin, CancellationToken.None);
            switch (resolution)
            {
                case CustomCommandViewerResolution.Found found
                    when command.AllowedUsers.Any(user =>
                        string.Equals(
                            user.TwitchUserId,
                            found.Viewer.TwitchUserId,
                            StringComparison.Ordinal
                        )
                    ):
                    command.AllowedUserFeedback = "That Twitch account is already selected.";
                    break;
                case CustomCommandViewerResolution.Found found:
                    var displayName = string.IsNullOrWhiteSpace(found.Viewer.DisplayName)
                        ? found.Viewer.Login
                        : found.Viewer.DisplayName;
                    command.AllowedUsers.Add(
                        new(found.Viewer.TwitchUserId, found.Viewer.Login, displayName)
                    );
                    command.AllowedUserLoginDraft = string.Empty;
                    command.AllowedUserFeedback = $"Added {displayName}.";
                    break;
                case CustomCommandViewerResolution.NotFound:
                    command.AllowedUserFeedback = "No Twitch account matches that login.";
                    break;
                case CustomCommandViewerResolution.Unavailable:
                    command.AllowedUserFeedback =
                        "Twitch account lookup is unavailable. Try again later.";
                    break;
            }
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(AddAllowedUserAsync), exception);
            command.AllowedUserFeedback = "Twitch account lookup is unavailable. Try again later.";
        }
        finally
        {
            command.AllowedUserLookupInProgress = false;
        }
    }

    private static void RemoveAllowedUser(
        CustomCommandEditor command,
        CustomCommandAllowedUserEditor user
    )
    {
        _ = command.AllowedUsers.Remove(user);
        command.AllowedUserFeedback = $"Removed {user.DisplayName}.";
    }

    private static bool IsValidTwitchLogin(string login) =>
        login.Length is > 0 and <= 25
        && login.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private Task TestCueAsync(OverlayCueCustomCommandActionEditor action) =>
        ObserveUiOperationAsync(
            nameof(TestCueAsync),
            async () =>
            {
                if (HostId == 0)
                {
                    return;
                }
                var outcome = await _execution.TestCueAsync(HostId, action, CancellationToken.None);
                _cueTestOutcome = CueAdmissionMessage(outcome);
            }
        );

    private static string CueAdmissionMessage(OverlayCueAdmissionOutcome outcome) =>
        outcome switch
        {
            OverlayCueAdmissionOutcome.Running => "Cue test started.",
            OverlayCueAdmissionOutcome.Queued => "Cue test queued.",
            OverlayCueAdmissionOutcome.Disconnected disconnected =>
                $"The overlay is disconnected. The cue is queued until {disconnected.ExpiresAtUtc.LocalDateTime:t}.",
            OverlayCueAdmissionOutcome.QueueRejected => "The overlay queue rejected this cue test.",
            OverlayCueAdmissionOutcome.Disabled =>
                "The selected overlay player or cue is disabled.",
            OverlayCueAdmissionOutcome.ParentDisabledOrCancelled =>
                "Custom commands or overlays are off. Turn both on in Channel setup.",
            OverlayCueAdmissionOutcome.Expired => "The cue test expired before it could play.",
            _ => "The selected overlay player or cue is no longer available.",
        };

    private Task ResetViewerAsync(CustomCommandEditor command) =>
        ObserveUiOperationAsync(nameof(ResetViewerAsync), () => ResetViewerCoreAsync(command));

    private async Task ResetViewerCoreAsync(CustomCommandEditor command)
    {
        if (HostId == 0 || command.Id <= 0)
        {
            return;
        }

        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var outcome = await _invocationResets.ResetViewerAsync(
                    HostId,
                    command.Id,
                    new CustomCommandResetActor(PageContext.Session.UserId, ActorLogin),
                    command.ResetViewerLogin,
                    CancellationToken.None
                );
                switch (outcome)
                {
                    case CustomCommandInvocationResetOutcome.Reset reset:
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>(
                                $"Reset {reset.AffectedClaimCount} lifetime viewer use{(reset.AffectedClaimCount == 1 ? string.Empty : "s")}."
                            )
                        );
                        break;
                    case CustomCommandInvocationResetOutcome.ViewerNotFound:
                        _ = _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(
                                "That Twitch viewer could not be found."
                            )
                        );
                        break;
                    case CustomCommandInvocationResetOutcome.CommandNotFound:
                        _ = _toasts.Publish(
                            new ToastRequest<ErrorToastStrategy>(
                                "That command is no longer available. Reload and try again."
                            )
                        );
                        break;
                }
            }
        );
    }

    private Task ResetAllViewersAsync(CustomCommandEditor command) =>
        ObserveUiOperationAsync(
            nameof(ResetAllViewersAsync),
            () => ResetAllViewersCoreAsync(command)
        );

    private void RequestResetAllViewers(CustomCommandEditor command) =>
        _pendingResetAllCommandId = command.Id > 0 ? command.Id : null;

    private void CancelResetAllViewers() => _pendingResetAllCommandId = null;

    private async Task ResetAllViewersCoreAsync(CustomCommandEditor command)
    {
        if (HostId == 0 || command.Id <= 0 || _pendingResetAllCommandId != command.Id)
        {
            return;
        }

        _pendingResetAllCommandId = null;

        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var outcome = await _invocationResets.ResetAllViewersAsync(
                    HostId,
                    command.Id,
                    new CustomCommandResetActor(PageContext.Session.UserId, ActorLogin),
                    CancellationToken.None
                );
                switch (outcome)
                {
                    case CustomCommandInvocationResetOutcome.Reset reset:
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>(
                                $"Reset {reset.AffectedClaimCount} lifetime viewer use{(reset.AffectedClaimCount == 1 ? string.Empty : "s")}."
                            )
                        );
                        break;
                    case CustomCommandInvocationResetOutcome.CommandNotFound:
                        _ = _toasts.Publish(
                            new ToastRequest<ErrorToastStrategy>(
                                "That command is no longer available. Reload and try again."
                            )
                        );
                        break;
                }
            }
        );
    }

    private void AddCounter()
    {
        if (_config is null)
        {
            return;
        }

        var counter = new CustomCounterEditor { Id = NextTemporaryId(), Name = "New counter" };
        _config.Counters.Add(counter);
        SwitchTab(CustomCommandSettingsTab.Commands);
        SelectEditor(CustomCommandEditorKind.Counter, counter.Id, CounterNameFieldId(counter));
    }

    private void RemoveCounter(CustomCounterEditor counter)
    {
        if (_config is null)
        {
            return;
        }

        if (
            _config.Commands.Any(x =>
                x.Action is CounterCustomCommandActionEditor action
                && action.CounterId == counter.Id
            )
        )
        {
            _ = _toasts.Publish(
                new ToastRequest<WarningToastStrategy>(
                    "This counter is used by a command. Change that command first, then delete it."
                )
            );
            return;
        }

        _ = _config.Counters.Remove(counter);
        EnsureEditorSelection();
    }

    private void AddAnnouncement()
    {
        if (_config is null || _config.MessageEntries.Count == 0)
        {
            return;
        }

        var announcement = new CustomAnnouncementEditor
        {
            Id = NextTemporaryId(),
            Name = "New scheduled message",
            MessageLibraryEntryId = _config.MessageEntries[0].Id,
            Schedule = new IntervalCustomAnnouncementScheduleEditor { IntervalMinutes = 30 },
        };
        _config.Announcements.Add(announcement);
        SwitchTab(CustomCommandSettingsTab.Commands);
        SelectEditor(
            CustomCommandEditorKind.ScheduledMessage,
            announcement.Id,
            AnnouncementNameFieldId(announcement)
        );
    }

    private void RemoveAnnouncement(CustomAnnouncementEditor announcement)
    {
        _ = (_config?.Announcements.Remove(announcement));
        EnsureEditorSelection();
    }

    private int NextTemporaryId() => _nextTemporaryId--;

    private CustomCommandEditorSelection? FirstCommandTabSelection() =>
        (_config?.Counters.FirstOrDefault(), _config?.Announcements.FirstOrDefault()) switch
        {
            ({ } counter, _) => new(CustomCommandEditorKind.Counter, counter.Id),
            (_, { } announcement) => new(CustomCommandEditorKind.ScheduledMessage, announcement.Id),
            _ => null,
        };

    private string _selectedTimeZoneLabel =>
        _config switch
        {
            null => string.Empty,
            { } config
                when _timeZones.FirstOrDefault(x => x.Id == config.TimeZoneId) is { } timeZone =>
                TimeZoneLabel(timeZone),
            { } config => config.TimeZoneId,
        };

    private static string CommandAdvancedSummary(CustomCommandEditor command)
    {
        var summaries = new List<string>();
        if (command.CooldownSeconds > 0)
        {
            summaries.Add($"{command.CooldownSeconds}s cooldown");
        }
        if (command.CooldownScope != CustomCommandCooldownScope.Global)
        {
            summaries.Add(CooldownScopeLabel(command.CooldownScope));
        }
        if (command.InvocationLimit != CustomCommandInvocationLimit.Unlimited)
        {
            summaries.Add(InvocationLimitLabel(command.InvocationLimit));
        }
        if (command.Action is CounterCustomCommandActionEditor counter)
        {
            summaries.Add($"Counter {counter.CounterId}");
        }
        if (command.Action is OverlayCueCustomCommandActionEditor)
        {
            summaries.Add("Overlay cue");
        }
        return summaries.Count == 0 ? "Default settings" : string.Join(" · ", summaries);
    }

    private static string CommandAccessSummary(CustomCommandEditor command) =>
        CustomCommandAccessPolicy.Describe(
            command.AllowEveryone,
            command.AllowModerators,
            command.AllowedUsers.Count
        );

    private static string SelectedUsersSummary(CustomCommandEditor command) =>
        command.AllowedUsers.Count == 0
            ? "Add specific Twitch accounts."
            : CountLabel(command.AllowedUsers.Count, "selected person");

    private static string CountLabel(int count, string singular) =>
        $"{count} {(count == 1 ? singular : singular + "s")}";

    private static string MessageSelectionLabel(CustomMessageSelectionMode mode) =>
        mode switch
        {
            CustomMessageSelectionMode.First => "Always use the first message",
            CustomMessageSelectionMode.Random => "Pick a message at random",
            CustomMessageSelectionMode.Sequential => "Use each message in order",
            _ => "Choose a message",
        };

    private static string CooldownScopeLabel(CustomCommandCooldownScope scope) =>
        scope switch
        {
            CustomCommandCooldownScope.Global => "Everyone shares the wait",
            CustomCommandCooldownScope.User => "Each viewer has their own wait",
            _ => "Choose who waits",
        };

    private static string InvocationLimitLabel(CustomCommandInvocationLimit limit) =>
        limit switch
        {
            CustomCommandInvocationLimit.Unlimited => "No use limit",
            CustomCommandInvocationLimit.OncePerStream => "Once each stream",
            CustomCommandInvocationLimit.OncePerUser => "Once per viewer (until reset)",
            CustomCommandInvocationLimit.OncePerStreamPerUser => "Once per viewer each stream",
            _ => "Choose a use limit",
        };

    private static string ActionKindLabel(CustomCommandActionKind action) =>
        action switch
        {
            CustomCommandActionKind.Message => "Send a reply",
            CustomCommandActionKind.Counter => "Add 1 to a counter, then send a reply",
            CustomCommandActionKind.OverlayCue => "Play an overlay cue",
            CustomCommandActionKind.Automation => "Saved action unavailable",
            _ => "Choose what happens",
        };

    private IEnumerable<CustomCommandActionKind> ActionKinds(CustomCommandEditor command) =>
        command.ActionKind == CustomCommandActionKind.Automation
            ? [.. _actionKinds, CustomCommandActionKind.Automation]
            : _actionKinds;

    private static string CueQueuePolicyLabel(OverlayCueQueuePolicy policy) =>
        policy switch
        {
            OverlayCueQueuePolicy.Enqueue => "Wait for earlier cues",
            OverlayCueQueuePolicy.Replace => "Replace the current cue",
            OverlayCueQueuePolicy.Ignore => "Ignore while the player is busy",
            OverlayCueQueuePolicy.Concurrent => "Play at the same time",
            _ => "Choose a playback policy",
        };

    private static string CueReplyOrderLabel(OverlayCueReplyOrder order) =>
        order switch
        {
            OverlayCueReplyOrder.Before => "Before requesting the cue",
            OverlayCueReplyOrder.After => "After the cue is accepted",
            _ => "Choose when to reply",
        };

    private static string AnnouncementScheduleLabel(CustomAnnouncementScheduleKind schedule) =>
        schedule switch
        {
            CustomAnnouncementScheduleKind.Interval => "On a timer",
            CustomAnnouncementScheduleKind.IntervalAfterChat => "On a timer, after chat activity",
            CustomAnnouncementScheduleKind.Weekly => "Once a week",
            _ => "Choose when to send",
        };

    private static string AnnouncementDeliveryTypeLabel(CustomAnnouncementDeliveryType type) =>
        type switch
        {
            CustomAnnouncementDeliveryType.ChatMessage => "Chat message",
            CustomAnnouncementDeliveryType.TwitchAnnouncement => "Twitch announcement",
            _ => "Choose delivery type",
        };

    private static string TwitchAnnouncementColorLabel(
        BlokeBot.Persistence.Models.TwitchAnnouncementColor color
    ) =>
        color switch
        {
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Primary => "Channel color",
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Blue => "Blue",
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Green => "Green",
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Orange => "Orange",
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Purple => "Purple",
            _ => "Choose color",
        };

    private static string LatestDeliveryResultLabel(
        CustomAnnouncementLatestDeliveryResult result
    ) =>
        result switch
        {
            CustomAnnouncementLatestDeliveryResult.None => "No delivery yet",
            CustomAnnouncementLatestDeliveryResult.Success => "Sent",
            CustomAnnouncementLatestDeliveryResult.Permission => "Permission needed",
            CustomAnnouncementLatestDeliveryResult.Invalid => "Invalid message",
            CustomAnnouncementLatestDeliveryResult.RateLimitRetry =>
                "Rate limited; retry scheduled",
            CustomAnnouncementLatestDeliveryResult.Unexpected => "Unexpected delivery failure",
            CustomAnnouncementLatestDeliveryResult.Ambiguous =>
                "Delivery may have happened; not retried",
            _ => "Unknown delivery result",
        };

    private static string TwitchAnnouncementCapabilityMessage(
        TwitchAnnouncementReadiness readiness
    ) =>
        readiness.Availability switch
        {
            TwitchAnnouncementAvailability.Available =>
                "Native Twitch announcements are ready for the active bot account.",
            TwitchAnnouncementAvailability.ReconnectRequired =>
                "This native announcement is inactive until the active bot reconnects with Twitch announcement permission.",
            TwitchAnnouncementAvailability.AuthorityRequired =>
                "This native announcement is inactive until the active bot is the broadcaster or a channel moderator.",
            TwitchAnnouncementAvailability.Unavailable =>
                "This native announcement is inactive while BlokeBot cannot verify the active bot's Twitch authority.",
            _ => "This native announcement is inactive.",
        };

    private bool NativeDeliveryUnavailable(CustomAnnouncementEditor announcement) =>
        announcement.DeliveryType == CustomAnnouncementDeliveryType.TwitchAnnouncement
        && _config?.TwitchAnnouncementReadiness.Availability
            != TwitchAnnouncementAvailability.Available;

    private static string TimeZoneLabel(TimeZoneInfo timeZone) => timeZone.DisplayName;

    private static string AlertImportanceLabel(DurableAlertSeverity severity) =>
        severity switch
        {
            DurableAlertSeverity.Critical => "Urgent",
            DurableAlertSeverity.Warning => "Warning",
            _ => "Information",
        };

    private static string FormatLastSent(DateTime? value) =>
        value is null
            ? "Never"
            : value.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
