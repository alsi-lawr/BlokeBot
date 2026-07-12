using BlokeBot.Eventing;
using BlokeBot.Components;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Features.CustomCommands;

public partial class CustomCommandSettingsPage
{
    private static readonly IReadOnlyList<CustomMessageSelectionMode> MessageSelectionModes =
        Enum.GetValues<CustomMessageSelectionMode>();
    private static readonly IReadOnlyList<CustomCommandCooldownScope> CooldownScopes =
        Enum.GetValues<CustomCommandCooldownScope>();
    private static readonly IReadOnlyList<CustomCommandActionKind> ActionKinds =
        Enum.GetValues<CustomCommandActionKind>();
    private static readonly IReadOnlyList<CustomAnnouncementScheduleKind> AnnouncementScheduleKinds =
        Enum.GetValues<CustomAnnouncementScheduleKind>();
    private static readonly IReadOnlyList<DayOfWeek> DaysOfWeek = Enum.GetValues<DayOfWeek>();
    private static readonly IReadOnlyList<TimeZoneInfo> TimeZones =
        TimeZoneInfo.GetSystemTimeZones();

    private CustomCommandConfiguration? config;
    private bool featureEnabled;
    private int nextTemporaryId = -1;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            Events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.CustomCommandsChanged],
                work => InvokeAsync(work),
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await LoadPageContextAsync();
        featureEnabled =
            HostId != 0
            && await Features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.CustomCommands,
                CancellationToken.None
            );
        config = featureEnabled
            ? await Configuration.LoadConfigurationAsync(HostId, CancellationToken.None)
            : null;
        nextTemporaryId = -1;
    }

    private async Task SaveAsync()
    {
        if (config is null || HostId == 0)
            return;

        try
        {
            await Configuration.SaveConfigurationAsync(HostId, config, CancellationToken.None);
            config = await Configuration.LoadConfigurationAsync(HostId, CancellationToken.None);
            nextTemporaryId = -1;
            Toasts.Success("Custom commands saved.");
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            Toasts.Error(ex.Message);
        }
    }

    private void AddMessageEntry()
    {
        if (config is null)
            return;

        config.MessageEntries.Add(
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
        if (config is null)
            return;

        if (
            config.Commands.Any(x => x.Action.MessageLibraryEntryId == entry.Id)
            || config.Announcements.Any(x => x.MessageLibraryEntryId == entry.Id)
        )
        {
            Toasts.Warning(
                "This reply is used by a command or announcement. Change that first, then delete it."
            );
            return;
        }

        config.MessageEntries.Remove(entry);
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
            Toasts.Warning("A reply needs at least one message.");
            return;
        }

        entry.Variants.Remove(variant);
        entry.CurrentVariantIndex = Math.Min(entry.CurrentVariantIndex, entry.Variants.Count - 1);
    }

    private static void MoveVariant(
        CustomMessageLibraryEntryEditor entry,
        int index,
        int direction
    )
    {
        var nextIndex = index + direction;
        if (nextIndex < 0 || nextIndex >= entry.Variants.Count)
            return;

        (entry.Variants[index], entry.Variants[nextIndex]) = (
            entry.Variants[nextIndex],
            entry.Variants[index]
        );
        entry.CurrentVariantIndex = Math.Min(entry.CurrentVariantIndex, entry.Variants.Count - 1);
    }

    private void AddCommand()
    {
        if (config is null || config.MessageEntries.Count == 0)
            return;

        config.Commands.Add(
            new CustomCommandEditor
            {
                Id = NextTemporaryId(),
                Name = "New command",
                Aliases = "newcommand",
                Action = new MessageCustomCommandActionEditor
                {
                    MessageLibraryEntryId = config.MessageEntries[0].Id,
                },
            }
        );
    }

    private void RemoveCommand(CustomCommandEditor command)
    {
        config?.Commands.Remove(command);
    }

    private void AddCounter()
    {
        if (config is null)
            return;

        config.Counters.Add(
            new CustomCounterEditor
            {
                Id = NextTemporaryId(),
                Name = "New counter",
            }
        );
    }

    private void RemoveCounter(CustomCounterEditor counter)
    {
        if (config is null)
            return;

        if (
            config.Commands.Any(x =>
                x.Action is CounterCustomCommandActionEditor action
                && action.CounterId == counter.Id
            )
        )
        {
            Toasts.Warning(
                "This counter is used by a command. Change that command first, then delete it."
            );
            return;
        }

        config.Counters.Remove(counter);
    }

    private void AddAnnouncement()
    {
        if (config is null || config.MessageEntries.Count == 0)
            return;

        config.Announcements.Add(
            new CustomAnnouncementEditor
            {
                Id = NextTemporaryId(),
                Name = "New announcement",
                MessageLibraryEntryId = config.MessageEntries[0].Id,
                RetryDelaySeconds = 0,
                OccurrenceLifetimeSeconds = 0,
                Schedule = new IntervalCustomAnnouncementScheduleEditor
                {
                    IntervalMinutes = 30,
                },
            }
        );
    }

    private void RemoveAnnouncement(CustomAnnouncementEditor announcement)
    {
        config?.Announcements.Remove(announcement);
    }

    private int NextTemporaryId() => nextTemporaryId--;

    private string SelectedTimeZoneLabel =>
        config is null
            ? string.Empty
            : TimeZones.FirstOrDefault(x => x.Id == config.TimeZoneId) is { } timeZone
                ? TimeZoneLabel(timeZone)
                : config.TimeZoneId;

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

    private static string ActionKindLabel(CustomCommandActionKind action) =>
        action switch
        {
            CustomCommandActionKind.Message => "Send a reply",
            CustomCommandActionKind.Counter => "Add 1 to a counter, then send a reply",
            _ => "Choose what happens",
        };

    private static string AnnouncementScheduleLabel(CustomAnnouncementScheduleKind schedule) =>
        schedule switch
        {
            CustomAnnouncementScheduleKind.Interval => "On a timer",
            CustomAnnouncementScheduleKind.IntervalAfterChat =>
                "On a timer, after chat activity",
            CustomAnnouncementScheduleKind.Weekly => "Once a week",
            _ => "Choose when to send",
        };

    private static string TimeZoneLabel(TimeZoneInfo timeZone) => timeZone.DisplayName;

    private static string AlertImportanceLabel(DurableAlertSeverity severity) =>
        severity switch
        {
            DurableAlertSeverity.Critical => "Urgent",
            DurableAlertSeverity.Warning => "Warning",
            _ => "Information",
        };

    private static string FormatLastSent(DateTime? value) =>
        value is null ? "Never" : value.Value.ToString("yyyy-MM-dd HH:mm 'UTC'");
}
