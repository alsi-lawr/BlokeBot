using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Profiles;

internal sealed record GuessingReplySettingsResolution(
    int ProfileId,
    GuessingReplySettings Settings,
    ReplyDeliveryMap ReplyDelivery
);

internal static class GuessingReplySettingsQueries
{
    public static Task<GuessingReplySettingsResolution> LoadForRoundAsync(
        BlokeBotDbContext db,
        int hostId,
        int roundProfileId,
        CancellationToken ct
    ) => LoadAsync(db, hostId, roundProfileId, ct);

    public static Task<GuessingReplySettingsResolution> LoadForProfileAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CancellationToken ct
    ) => LoadAsync(db, hostId, profileId, ct);

    public static async Task<GuessingReplySettingsResolution> LoadForDefaultAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) => await LoadAsync(db, hostId, await db.Profiles.LoadDefaultProfileIdAsync(hostId, ct), ct);

    private static async Task<GuessingReplySettingsResolution> LoadAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        var settings = await db
            .Profiles.AsNoTracking()
            .Where(profile => profile.Id == profileId && profile.HostId == hostId)
            .Select(profile => profile.ReplySettings)
            .SingleOrDefaultAsync(ct);
        var delivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyFeature.Guessing,
            profileId,
            ct
        );
        return new GuessingReplySettingsResolution(
            profileId,
            GuessingReplySettingsMapper.FromPersistence(settings),
            delivery
        );
    }
}

internal static class GuessingReplySettingsMapper
{
    public static GuessingReplySettings FromPersistence(BotReplySettings? settings)
    {
        var value = settings ?? ReplySettingsMapper.ToEntity(GuessingDefaults.Replies());
        return new GuessingReplySettings(
            value.RoundStartedReply,
            value.RoundAlreadyOpenReply,
            value.NoOpenRoundReply,
            value.GuessingStoppedReply,
            value.GuessingAlreadyStoppedReply,
            value.GuessingClosedReply,
            value.InvalidGuessReply,
            value.GuessUsageReply,
            value.AvailableGuessesReply,
            value.WinUsageReply,
            value.ModeratorOnlyReply,
            value.WinnerReply,
            value.NoWinnersReply
        );
    }
}
