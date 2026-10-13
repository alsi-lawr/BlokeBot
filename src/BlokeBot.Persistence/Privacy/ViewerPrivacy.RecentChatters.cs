using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static Task ExportRecentChattersAsync(ExportContext context) =>
        AddExportSectionAsync(
            context,
            "recent_channel_chatters",
            context.Db.RecentChannelChatters.Where(x =>
                x.TwitchUserId == context.UserId
                && (context.HostId == null || x.HostId == context.HostId)
            )
        );

    private static async Task EraseRecentChattersAsync(ErasureContext context) =>
        Record(
            context,
            "recent_channel_chatters",
            await context
                .Db.RecentChannelChatters.Where(x =>
                    x.TwitchUserId == context.UserId
                    && (context.HostId == null || x.HostId == context.HostId)
                )
                .ExecuteDeleteAsync(context.CancellationToken)
        );
}
