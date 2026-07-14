using System.Collections.Immutable;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Profiles;

internal sealed record GuessingReplySettings(
    string RoundStartedReply,
    string RoundAlreadyOpenReply,
    string NoOpenRoundReply,
    string GuessingStoppedReply,
    string GuessingAlreadyStoppedReply,
    string GuessingClosedReply,
    string InvalidGuessReply,
    string GuessUsageReply,
    string AvailableGuessesReply,
    string WinUsageReply,
    string ModeratorOnlyReply,
    string WinnerReply,
    string NoWinnersReply
);

internal sealed record GuessingReplySettingsResolution(
    int ProfileId,
    GuessingReplySettings Settings,
    ReplyDeliveryMap ReplyDelivery
);

internal sealed record GuessRoundProfileDetails(
    int Id,
    string Name,
    GuessingReplySettings Settings,
    ImmutableArray<string> OptionNames
);

internal static class GuessingProfileQueries
{
    public static async Task<int> DefaultProfileIdAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        return await db
            .Profiles.Where(x => x.HostId == hostId && x.IsDefault)
            .Select(x => x.Id)
            .FirstAsync(ct);
    }

    public static async Task<int?> LoadProfileIdByNameAsync(
        BlokeBotDbContext db,
        int hostId,
        string profileName,
        CancellationToken ct
    )
    {
        return await db
            .Profiles.AsNoTracking()
            .Where(x =>
                x.HostId == hostId && x.Slug == GuessRoundProfileSlug.FromName(profileName).Value
            )
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
    }

    public static async Task<GuessRoundProfileDetails?> LoadProfileDetailsAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        var profile = await db
            .Profiles.AsNoTracking()
            .Include(x => x.ReplySettings)
            .Include(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == profileId && x.HostId == hostId, ct);
        return profile is null
            ? null
            : new GuessRoundProfileDetails(
                profile.Id,
                profile.Name,
                ToDomain(profile.ReplySettings ?? DefaultSettings()),
                profile.Options.OrderBy(x => x.Name).Select(x => x.Name).ToImmutableArray()
            );
    }

    public static async Task<GuessingReplySettingsResolution> ResolveReplySettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        int? roundProfileId,
        int? requestedProfileId,
        CancellationToken ct
    )
    {
        var selectedProfileId =
            roundProfileId ?? requestedProfileId ?? await DefaultProfileIdAsync(db, hostId, ct);
        var settings = await db
            .Profiles.AsNoTracking()
            .Where(x => x.Id == selectedProfileId && x.HostId == hostId)
            .Select(x => x.ReplySettings)
            .SingleOrDefaultAsync(ct);
        var delivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyFeature.Guessing,
            selectedProfileId,
            ct
        );
        return new GuessingReplySettingsResolution(
            selectedProfileId,
            ToDomain(settings ?? DefaultSettings()),
            delivery
        );
    }

    public static GuessingReplySettings DefaultReplySettings()
    {
        return ToDomain(DefaultSettings());
    }

    private static BotReplySettings DefaultSettings()
    {
        return ReplySettingsMapper.ToEntity(GuessingDefaults.Replies());
    }

    private static GuessingReplySettings ToDomain(BotReplySettings settings)
    {
        return new(
            settings.RoundStartedReply,
            settings.RoundAlreadyOpenReply,
            settings.NoOpenRoundReply,
            settings.GuessingStoppedReply,
            settings.GuessingAlreadyStoppedReply,
            settings.GuessingClosedReply,
            settings.InvalidGuessReply,
            settings.GuessUsageReply,
            settings.AvailableGuessesReply,
            settings.WinUsageReply,
            settings.ModeratorOnlyReply,
            settings.WinnerReply,
            settings.NoWinnersReply
        );
    }
}
