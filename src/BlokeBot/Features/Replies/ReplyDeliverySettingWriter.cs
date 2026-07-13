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
        ReplyFeature feature,
        int scopeId,
        CancellationToken ct
    )
    {
        var scopedSettings = await db
            .ReplyDeliverySettings.AsNoTracking()
            .Where(x => x.HostId == hostId && x.ScopeId == scopeId)
            .ToListAsync(ct);
        return ReplyDeliveryMap.FromSettings(scopedSettings.Where(x => x.Feature == feature));
    }

    public static async Task ReplaceAsync(
        BlokeBotDbContext db,
        int hostId,
        ReplyFeature feature,
        int scopeId,
        ReplyDeliveryMap delivery,
        CancellationToken ct
    )
    {
        var scopedSettings = await db
            .ReplyDeliverySettings.Where(x => x.HostId == hostId && x.ScopeId == scopeId)
            .ToListAsync(ct);
        var existing = scopedSettings.Where(x => x.Feature == feature);
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
                    Target = ReplyDeliveryTarget.Whisper,
                }
            );
        }
    }
}
