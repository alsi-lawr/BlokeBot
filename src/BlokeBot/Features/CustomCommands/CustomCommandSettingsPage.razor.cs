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
    private static readonly IReadOnlyList<CustomCommandActionType> ActionTypes =
        Enum.GetValues<CustomCommandActionType>();
    private static readonly IReadOnlyList<CustomAnnouncementScheduleType> AnnouncementScheduleTypes =
        Enum.GetValues<CustomAnnouncementScheduleType>();
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
            Toasts.Success("Settings saved.");
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
                Name = "New message",
                Variants =
                [
                    new CustomMessageVariantEditor
                    {
                        Id = NextTemporaryId(),
                        Text = "Message text",
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
            config.Commands.Any(x => x.MessageLibraryEntryId == entry.Id)
            || config.Announcements.Any(x => x.MessageLibraryEntryId == entry.Id)
        )
        {
            Toasts.Warning("Message entries used by commands or announcements cannot be deleted.");
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
                Text = "Message text",
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
            Toasts.Warning("Message entries need at least one variant.");
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
                MessageLibraryEntryId = config.MessageEntries[0].Id,
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

        if (config.Commands.Any(x => x.CounterId == counter.Id))
        {
            Toasts.Warning("Counters used by commands cannot be deleted.");
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
                IntervalMinutes = 30,
            }
        );
    }

    private void RemoveAnnouncement(CustomAnnouncementEditor announcement)
    {
        config?.Announcements.Remove(announcement);
    }

    private static void SetWeeklyDay(
        CustomAnnouncementEditor announcement,
        ChangeEventArgs args
    )
    {
        announcement.WeeklyDay = Enum.TryParse<DayOfWeek>(
            args.Value?.ToString(),
            out var day
        )
            ? day
            : null;
    }

    private int NextTemporaryId() => nextTemporaryId--;

    private static string FormatLastSent(DateTime? value) =>
        value is null ? "Never" : value.Value.ToString("yyyy-MM-dd HH:mm 'UTC'");
}
