using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Profiles;

internal static class GuessingProfileQueries
{
    public static async Task<int> DefaultProfileIdAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .Profiles.Where(x => x.HostId == hostId && x.IsDefault)
            .Select(x => x.Id)
            .FirstAsync(ct);

    public static async Task<GuessRoundProfile> DefaultProfileWithSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct,
        bool includeOptions = false
    ) =>
        await LoadProfileWithSettingsAsync(
            db,
            hostId,
            await DefaultProfileIdAsync(db, hostId, ct),
            ct,
            includeOptions
        ) ?? throw new InvalidOperationException("Default profile is missing.");

    public static async Task<GuessRoundProfile?> LoadProfileByNameAsync(
        BlokeBotDbContext db,
        int hostId,
        string profileName,
        CancellationToken ct
    ) =>
        await db
            .Profiles.AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.HostId == hostId
                    && x.Slug == GuessRoundProfileSlug.FromName(profileName).Value,
                ct
            );

    public static async Task<GuessRoundProfile?> LoadProfileWithSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CancellationToken ct,
        bool includeOptions = false
    )
    {
        IQueryable<GuessRoundProfile> query = db.Profiles.Include(x => x.ReplySettings);
        if (includeOptions)
            query = query.Include(x => x.Options);

        return await query.SingleOrDefaultAsync(x => x.Id == profileId && x.HostId == hostId, ct);
    }

    public static async Task<BotReplySettings> ReplySettingsForRoundOrDefaultAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessRound? round,
        CancellationToken ct
    ) => await ReplySettingsForRoundOrProfileOrDefaultAsync(db, hostId, round, null, ct);

    public static async Task<BotReplySettings> ReplySettingsForRoundOrProfileOrDefaultAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessRound? round,
        int? profileId,
        CancellationToken ct
    )
    {
        var selectedProfileId =
            round?.GuessRoundProfileId ?? profileId ?? await DefaultProfileIdAsync(db, hostId, ct);
        var profile = await LoadProfileWithSettingsAsync(db, hostId, selectedProfileId, ct);
        return profile?.ReplySettings ?? ReplySettingsMapper.ToEntity(GuessingDefaults.Replies());
    }
}
