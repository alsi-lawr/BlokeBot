using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CustomCommands;

public partial class CustomCommandSettingsPage
{
    private void ChangeTimeZone(ChangeEventArgs args)
    {
        if (_config is not null && args.Value?.ToString() is { } timeZoneId)
        {
            WeeklyAnnouncementScheduleEditorProjection.ChangeTimeZone(_config, timeZoneId);
        }
    }
}
