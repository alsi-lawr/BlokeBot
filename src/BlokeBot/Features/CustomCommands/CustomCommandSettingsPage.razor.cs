using BlokeBot.Components;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Features.CustomCommands;

public partial class CustomCommandSettingsPage
{
    private static readonly IReadOnlyList<CustomMessageSelectionMode> _messageSelectionModes =
        Enum.GetValues<CustomMessageSelectionMode>();
    private static readonly IReadOnlyList<CustomCommandCooldownScope> _cooldownScopes =
        Enum.GetValues<CustomCommandCooldownScope>();
    private static readonly IReadOnlyList<CustomCommandActionKind> _actionKinds =
        Enum.GetValues<CustomCommandActionKind>();
    private static readonly IReadOnlyList<CustomAnnouncementScheduleKind> _announcementScheduleKinds =
        Enum.GetValues<CustomAnnouncementScheduleKind>();
    private static readonly IReadOnlyList<DayOfWeek> _daysOfWeek = Enum.GetValues<DayOfWeek>();
    private static readonly IReadOnlyList<TimeZoneInfo> _timeZones =
        TimeZoneInfo.GetSystemTimeZones();

    private CustomCommandConfiguration? _config;
    private bool _featureEnabled;
    private int _nextTemporaryId = -1;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.CustomCommandsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private Task LoadAsync()
    {
        return ObserveUiOperationAsync(nameof(LoadAsync), LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        await LoadPageContextAsync();
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.CustomCommands,
                CancellationToken.None
            );
        _config = _featureEnabled
            ? await _configuration.LoadConfigurationAsync(HostId, CancellationToken.None)
            : null;
        _nextTemporaryId = -1;
    }

    private Task SaveAsync()
    {
        return ObserveUiOperationAsync(nameof(SaveAsync), SaveCoreAsync);
    }

    private async Task SaveCoreAsync()
    {
        if (_config is null || HostId == 0)
        {
            return;
        }

        await CustomCommandConfigurationValidator
            .Validate(_config)
            .Match(
                SaveCommandAsync,
                errors =>
                {
                    _toasts.Publish(new ToastRequest<ErrorToastStrategy>(errors[0].Message));
                    return Task.CompletedTask;
                }
            );
    }

    private async Task SaveCommandAsync(CustomCommandConfigurationSaveCommand command)
    {
        var result = await _configuration
            .SaveConfiguration(HostId, command)
            .ExecuteAsync(CancellationToken.None);
        await result.Match(
            async _ =>
            {
                _config = await _configuration.LoadConfigurationAsync(
                    HostId,
                    CancellationToken.None
                );
                _nextTemporaryId = -1;
                _toasts.Publish(new ToastRequest<SuccessToastStrategy>("Custom commands saved."));
            },
            failure =>
            {
                _toasts.Publish(new ToastRequest<ErrorToastStrategy>(failure.Message));
                return Task.CompletedTask;
            }
        );
    }

    private void AddMessageEntry()
    {
        if (_config is null)
        {
            return;
        }

        _config.MessageEntries.Add(
            new CustomMessageLibraryEntryEditor
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
            }
        );
    }

    private void RemoveMessageEntry(CustomMessageLibraryEntryEditor entry)
    {
        if (_config is null)
        {
            return;
        }

        if (
            _config.Commands.Any(x => x.Action.MessageLibraryEntryId == entry.Id)
            || _config.Announcements.Any(x => x.MessageLibraryEntryId == entry.Id)
        )
        {
            _toasts.Publish(
                new ToastRequest<WarningToastStrategy>(
                    "This reply is used by a command or announcement. Change that first, then delete it."
                )
            );
            return;
        }

        _config.MessageEntries.Remove(entry);
    }

    private void AddVariant(CustomMessageLibraryEntryEditor entry)
    {
        entry.Variants.Add(
            new CustomMessageVariantEditor
            {
                Id = NextTemporaryId(),
                Text = "Type your reply here.",
            }
        );
    }

    private void RemoveVariant(
        CustomMessageLibraryEntryEditor entry,
        CustomMessageVariantEditor variant
    )
    {
        if (entry.Variants.Count <= 1)
        {
            _toasts.Publish(
                new ToastRequest<WarningToastStrategy>("A reply needs at least one message.")
            );
            return;
        }

        entry.Variants.Remove(variant);
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

        _config.Commands.Add(
            new CustomCommandEditor
            {
                Id = NextTemporaryId(),
                Name = "New command",
                Aliases = "newcommand",
                Action = new MessageCustomCommandActionEditor
                {
                    MessageLibraryEntryId = _config.MessageEntries[0].Id,
                },
            }
        );
    }

    private void RemoveCommand(CustomCommandEditor command)
    {
        _config?.Commands.Remove(command);
    }

    private void AddCounter()
    {
        if (_config is null)
        {
            return;
        }

        _config.Counters.Add(
            new CustomCounterEditor { Id = NextTemporaryId(), Name = "New counter" }
        );
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
            _toasts.Publish(
                new ToastRequest<WarningToastStrategy>(
                    "This counter is used by a command. Change that command first, then delete it."
                )
            );
            return;
        }

        _config.Counters.Remove(counter);
    }

    private void AddAnnouncement()
    {
        if (_config is null || _config.MessageEntries.Count == 0)
        {
            return;
        }

        _config.Announcements.Add(
            new CustomAnnouncementEditor
            {
                Id = NextTemporaryId(),
                Name = "New announcement",
                MessageLibraryEntryId = _config.MessageEntries[0].Id,
                RetryDelaySeconds = 0,
                OccurrenceLifetimeSeconds = 0,
                Schedule = new IntervalCustomAnnouncementScheduleEditor { IntervalMinutes = 30 },
            }
        );
    }

    private void RemoveAnnouncement(CustomAnnouncementEditor announcement)
    {
        _config?.Announcements.Remove(announcement);
    }

    private int NextTemporaryId()
    {
        return _nextTemporaryId--;
    }

    private string _selectedTimeZoneLabel =>
        _config is null ? string.Empty
        : _timeZones.FirstOrDefault(x => x.Id == _config.TimeZoneId) is { } timeZone
            ? TimeZoneLabel(timeZone)
        : _config.TimeZoneId;

    private static string CountLabel(int count, string singular)
    {
        return $"{count} {(count == 1 ? singular : singular + "s")}";
    }

    private static string MessageSelectionLabel(CustomMessageSelectionMode mode)
    {
        return mode switch
        {
            CustomMessageSelectionMode.First => "Always use the first message",
            CustomMessageSelectionMode.Random => "Pick a message at random",
            CustomMessageSelectionMode.Sequential => "Use each message in order",
            _ => "Choose a message",
        };
    }

    private static string CooldownScopeLabel(CustomCommandCooldownScope scope)
    {
        return scope switch
        {
            CustomCommandCooldownScope.Global => "Everyone shares the wait",
            CustomCommandCooldownScope.User => "Each viewer has their own wait",
            _ => "Choose who waits",
        };
    }

    private static string ActionKindLabel(CustomCommandActionKind action)
    {
        return action switch
        {
            CustomCommandActionKind.Message => "Send a reply",
            CustomCommandActionKind.Counter => "Add 1 to a counter, then send a reply",
            _ => "Choose what happens",
        };
    }

    private static string AnnouncementScheduleLabel(CustomAnnouncementScheduleKind schedule)
    {
        return schedule switch
        {
            CustomAnnouncementScheduleKind.Interval => "On a timer",
            CustomAnnouncementScheduleKind.IntervalAfterChat => "On a timer, after chat activity",
            CustomAnnouncementScheduleKind.Weekly => "Once a week",
            _ => "Choose when to send",
        };
    }

    private static string TimeZoneLabel(TimeZoneInfo timeZone)
    {
        return timeZone.DisplayName;
    }

    private static string AlertImportanceLabel(DurableAlertSeverity severity)
    {
        return severity switch
        {
            DurableAlertSeverity.Critical => "Urgent",
            DurableAlertSeverity.Warning => "Warning",
            _ => "Information",
        };
    }

    private static string FormatLastSent(DateTime? value)
    {
        return value is null ? "Never" : value.Value.ToString("yyyy-MM-dd HH:mm 'UTC'");
    }
}
