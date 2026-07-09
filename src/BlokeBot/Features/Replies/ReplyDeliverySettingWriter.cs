using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Replies;

internal static class ReplyDeliverySettingWriter
{
    public const int HostScopeId = 0;

    public static async Task<ReplyDeliveryMap> LoadAsync(
        BlokeBotDbContext db,
        int hostId,
        string feature,
        int scopeId,
        CancellationToken ct
    )
    {
        var settings = await db
            .ReplyDeliverySettings.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Feature == feature && x.ScopeId == scopeId)
            .ToListAsync(ct);
        return ReplyDeliveryMap.FromSettings(settings);
    }

    public static async Task ReplaceAsync(
        BlokeBotDbContext db,
        int hostId,
        string feature,
        int scopeId,
        ReplyDeliveryMap delivery,
        CancellationToken ct
    )
    {
        var existing = await db
            .ReplyDeliverySettings.Where(x =>
                x.HostId == hostId && x.Feature == feature && x.ScopeId == scopeId
            )
            .ToListAsync(ct);
        db.ReplyDeliverySettings.RemoveRange(existing);

        foreach (var replyKey in delivery.WhisperKeys.Order(StringComparer.OrdinalIgnoreCase))
        {
            db.ReplyDeliverySettings.Add(
                new ReplyDeliverySetting
                {
                    HostId = hostId,
                    Feature = feature,
                    ScopeId = scopeId,
                    ReplyKey = replyKey,
                    Target = ReplyDeliveryTargets.Whisper,
                }
            );
        }
    }
}
