using System.Collections.Immutable;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Profiles;

internal sealed record GuessRoundProfileDetails(
    int Id,
    string Name,
    GuessingReplySettings Settings
);

internal sealed record GuessRoundProfileDetailsWithOptions(
    int Id,
    string Name,
    GuessingReplySettings Settings,
    ImmutableArray<string> OptionNames
);

internal static class GuessRoundProfileQueryExtensions
{
    extension(IQueryable<GuessRoundProfile> profiles)
    {
        public Task<int> LoadDefaultProfileIdAsync(int hostId, CancellationToken ct)
        {
            return profiles
                .AsNoTracking()
                .Where(profile => profile.HostId == hostId && profile.IsDefault)
                .Select(profile => profile.Id)
                .FirstAsync(ct);
        }

        public Task<int?> LoadProfileIdByNameAsync(
            int hostId,
            string profileName,
            CancellationToken ct
        )
        {
            var slug = GuessRoundProfileSlug.FromName(profileName).Value;
            return profiles
                .AsNoTracking()
                .Where(profile => profile.HostId == hostId && profile.Slug == slug)
                .Select(profile => (int?)profile.Id)
                .SingleOrDefaultAsync(ct);
        }

        public async Task<GuessRoundProfileDetails> LoadDefaultProfileAsync(
            int hostId,
            CancellationToken ct
        )
        {
            var profileId = await profiles.LoadDefaultProfileIdAsync(hostId, ct);
            return await profiles.LoadProfileAsync(hostId, profileId, ct)
                ?? throw new InvalidOperationException("Default profile is missing.");
        }

        public async Task<GuessRoundProfileDetailsWithOptions> LoadDefaultProfileWithOptionsAsync(
            int hostId,
            CancellationToken ct
        )
        {
            var profileId = await profiles.LoadDefaultProfileIdAsync(hostId, ct);
            return await profiles.LoadProfileWithOptionsAsync(hostId, profileId, ct)
                ?? throw new InvalidOperationException("Default profile is missing.");
        }

        public async Task<GuessRoundProfileDetails?> LoadProfileAsync(
            int hostId,
            int profileId,
            CancellationToken ct
        )
        {
            var profile = await profiles
                .AsNoTracking()
                .Include(candidate => candidate.ReplySettings)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == profileId && candidate.HostId == hostId,
                    ct
                );
            return profile is null
                ? null
                : new GuessRoundProfileDetails(
                    profile.Id,
                    profile.Name,
                    GuessingReplySettingsMapper.FromPersistence(profile.ReplySettings)
                );
        }

        public async Task<GuessRoundProfileDetailsWithOptions?> LoadProfileWithOptionsAsync(
            int hostId,
            int profileId,
            CancellationToken ct
        )
        {
            var profile = await profiles
                .AsNoTracking()
                .Include(candidate => candidate.ReplySettings)
                .Include(candidate => candidate.Options)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == profileId && candidate.HostId == hostId,
                    ct
                );
            return profile is null
                ? null
                : new GuessRoundProfileDetailsWithOptions(
                    profile.Id,
                    profile.Name,
                    GuessingReplySettingsMapper.FromPersistence(profile.ReplySettings),
                    profile
                        .Options.OrderBy(option => option.Name)
                        .Select(option => option.Name)
                        .ToImmutableArray()
                );
        }
    }
}
