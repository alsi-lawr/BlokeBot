using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ViewerPassports;

public sealed class ViewerPassportService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointBalanceService balances,
    TimeProvider clock
)
{
    private readonly SemaphoreSlim[] _mutationGates = Enumerable
        .Range(0, 64)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task<ViewerPassportQueryOutcome> GetSelfAsync(
        int hostId,
        ViewerPassportIdentity viewer,
        CancellationToken cancellationToken
    )
    {
        var normalized = Normalize(viewer);
        if (normalized is null)
        {
            return new ViewerPassportQueryOutcome.NotFound();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await EnabledHostAsync(db, hostId, cancellationToken);
        if (host is null)
        {
            return await HostExistsAsync(db, hostId, cancellationToken)
                ? new ViewerPassportQueryOutcome.FeatureDisabled()
                : new ViewerPassportQueryOutcome.NotFound();
        }

        var passport = await db
            .ViewerPassports.AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.HostId == hostId && value.TwitchUserId == normalized.TwitchUserId,
                cancellationToken
            );
        passport ??= DraftPassport(hostId, normalized);
        return new ViewerPassportQueryOutcome.Available(
            await ToViewAsync(db, host, passport, includeEarnedRewards: true, cancellationToken)
        );
    }

    public async Task<ViewerPassportQueryOutcome> GetSelfAsync(
        string channelLogin,
        ViewerPassportIdentity viewer,
        CancellationToken cancellationToken
    )
    {
        var channel = NormalizeLogin(channelLogin);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Login == channel)
            .Select(value => new HostView(
                value.Id,
                value.Login,
                value.DisplayName,
                value.EnabledFeatures
            ))
            .SingleOrDefaultAsync(cancellationToken);
        return host is null
            ? new ViewerPassportQueryOutcome.NotFound()
            : !host.EnabledFeatures.Contains(HostFeatureFlags.ViewerPassports)
                ? new ViewerPassportQueryOutcome.FeatureDisabled()
                : await GetSelfAsync(host.Id, viewer, cancellationToken);
    }

    public async Task<ViewerPassportQueryOutcome> GetVisibleAsync(
        string channelLogin,
        string viewerLogin,
        ViewerPassportAudience audience,
        CancellationToken cancellationToken
    )
    {
        var channel = NormalizeLogin(channelLogin);
        var viewer = NormalizeLogin(viewerLogin);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Login == channel)
            .Select(value => new HostView(
                value.Id,
                value.Login,
                value.DisplayName,
                value.EnabledFeatures
            ))
            .SingleOrDefaultAsync(cancellationToken);
        if (host is null)
        {
            return new ViewerPassportQueryOutcome.NotFound();
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.ViewerPassports))
        {
            return new ViewerPassportQueryOutcome.FeatureDisabled();
        }

        var passport = await db
            .ViewerPassports.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.HostId == host.Id && value.Login == viewer,
                cancellationToken
            );
        return passport is null
            ? new ViewerPassportQueryOutcome.NotFound()
            : !await CanViewAsync(db, passport, audience, cancellationToken)
                ? new ViewerPassportQueryOutcome.Forbidden()
                : new ViewerPassportQueryOutcome.Available(
                    await ToViewAsync(
                        db,
                        host,
                        passport,
                        includeEarnedRewards: false,
                        cancellationToken
                    )
                );
    }

    public async Task<ViewerPassportMutationOutcome> SaveAsync(
        SaveViewerPassportCommand command,
        CancellationToken cancellationToken
    )
    {
        var identity = Normalize(command.Viewer);
        var profileLine = NormalizeProfileLine(command.ProfileLine);
        if (identity is null)
        {
            return new ViewerPassportMutationOutcome.Invalid(
                "A Twitch user ID and login are required."
            );
        }
        if (profileLine is null)
        {
            return new ViewerPassportMutationOutcome.Invalid(
                $"Profile lines must be a single line of {ViewerPassportLimits.ProfileLineMaximumLength} characters or fewer."
            );
        }
        if (!Enum.IsDefined(command.Visibility))
        {
            return new ViewerPassportMutationOutcome.Invalid("Choose a supported visibility.");
        }

        var gate = Gate(identity.TwitchUserId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var host = await EnabledHostAsync(db, command.HostId, cancellationToken);
            if (host is null)
            {
                return await HostExistsAsync(db, command.HostId, cancellationToken)
                    ? new ViewerPassportMutationOutcome.FeatureDisabled()
                    : new ViewerPassportMutationOutcome.NotFound();
            }
            if (
                !await RewardsAreEarnedAsync(
                    db,
                    command.HostId,
                    identity.TwitchUserId,
                    command.SelectedTitleRewardId,
                    command.SelectedBadgeRewardId,
                    cancellationToken
                )
            )
            {
                return new ViewerPassportMutationOutcome.UnearnedReward();
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var passport = await db.ViewerPassports.SingleOrDefaultAsync(
                value =>
                    value.HostId == command.HostId
                    && value.TwitchUserId == identity.TwitchUserId,
                cancellationToken
            );
            if (passport is null)
            {
                passport = new ViewerPassport
                {
                    HostId = command.HostId,
                    TwitchUserId = identity.TwitchUserId,
                    CreatedAtUtc = now,
                };
                _ = db.ViewerPassports.Add(passport);
            }

            await DetachReusedLoginAsync(
                db,
                command.HostId,
                identity.TwitchUserId,
                identity.Login,
                now,
                cancellationToken
            );
            passport.Login = identity.Login;
            passport.DisplayName = identity.DisplayName;
            passport.ProfileLine = profileLine;
            passport.Visibility = command.Visibility;
            passport.HideAttendance = command.HideAttendance;
            passport.SelectedTitleRewardDefinitionId = command.SelectedTitleRewardId;
            passport.SelectedBadgeRewardDefinitionId = command.SelectedBadgeRewardId;
            passport.UpdatedAtUtc = now;
            _ = await db.SaveChangesAsync(cancellationToken);
            return new ViewerPassportMutationOutcome.Succeeded(
                await ToViewAsync(db, host, passport, includeEarnedRewards: true, cancellationToken)
            );
        }
        finally
        {
            _ = gate.Release();
        }
    }

    public async Task<bool> RecordChatPresenceAsync(
        string channelLogin,
        ViewerPassportIdentity viewer,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken
    )
    {
        var identity = Normalize(viewer);
        if (identity is null)
        {
            return false;
        }
        var gate = Gate(identity.TwitchUserId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var channel = NormalizeLogin(channelLogin);
            var host = await db
                .Hosts.SingleOrDefaultAsync(
                    value =>
                        value.Login == channel
                        && (value.EnabledFeatures & HostFeatureFlags.ViewerPassports)
                            == HostFeatureFlags.ViewerPassports,
                    cancellationToken
                );
            if (host is null)
            {
                return false;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var passport = await db.ViewerPassports.SingleOrDefaultAsync(
                value =>
                    value.HostId == host.Id && value.TwitchUserId == identity.TwitchUserId,
                cancellationToken
            );
            if (passport is null)
            {
                passport = new ViewerPassport
                {
                    HostId = host.Id,
                    TwitchUserId = identity.TwitchUserId,
                    Visibility = ViewerPassportVisibility.Private,
                    HideAttendance = true,
                    CreatedAtUtc = now,
                };
                _ = db.ViewerPassports.Add(passport);
            }

            await DetachReusedLoginAsync(
                db,
                host.Id,
                identity.TwitchUserId,
                identity.Login,
                now,
                cancellationToken
            );
            passport.Login = identity.Login;
            passport.DisplayName = identity.DisplayName;
            passport.UpdatedAtUtc = now;
            _ = await db.SaveChangesAsync(cancellationToken);

            var date = DateOnly.FromDateTime(occurredAtUtc.UtcDateTime);
            var recorded = await db.ViewerPassportAttendanceDays.AnyAsync(
                value =>
                    value.HostId == host.Id
                    && value.PassportId == passport.Id
                    && value.DateUtc == date,
                cancellationToken
            );
            if (recorded)
            {
                return false;
            }
            _ = db.ViewerPassportAttendanceDays.Add(
                new()
                {
                    HostId = host.Id,
                    PassportId = passport.Id,
                    DateUtc = date,
                    FirstSeenAtUtc = occurredAtUtc.UtcDateTime,
                }
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            _ = gate.Release();
        }
    }

    public async Task<ViewerPassportResetOutcome> ResetAsync(
        int hostId,
        string twitchUserId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(value => value.Id == hostId, cancellationToken);
        if (host is null)
        {
            return new ViewerPassportResetOutcome.NotFound();
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.ViewerPassports))
        {
            return new ViewerPassportResetOutcome.FeatureDisabled();
        }
        var passport = await db.ViewerPassports.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.TwitchUserId == twitchUserId,
            cancellationToken
        );
        if (passport is null)
        {
            return new ViewerPassportResetOutcome.Succeeded(false);
        }
        _ = db.ViewerPassports.Remove(passport);
        _ = await db.SaveChangesAsync(cancellationToken);
        return new ViewerPassportResetOutcome.Succeeded(true);
    }

    public async Task<ViewerPassportExportOutcome> ExportAsync(
        int hostId,
        ViewerPassportIdentity viewer,
        CancellationToken cancellationToken
    )
    {
        var identity = Normalize(viewer);
        if (identity is null)
        {
            return new ViewerPassportExportOutcome.NotFound();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == hostId,
            cancellationToken
        );
        if (host is null)
        {
            return new ViewerPassportExportOutcome.NotFound();
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.ViewerPassports))
        {
            return new ViewerPassportExportOutcome.FeatureDisabled();
        }
        var export = await ViewerPrivacyService.ExportAsync(
            db,
            PrivacySubject.Create(identity.TwitchUserId, identity.Login),
            hostId,
            cancellationToken
        );
        return new ViewerPassportExportOutcome.Succeeded(export.Sections);
    }

    private async Task<ViewerPassportView> ToViewAsync(
        BlokeBotDbContext db,
        HostView host,
        ViewerPassport passport,
        bool includeEarnedRewards,
        CancellationToken cancellationToken
    )
    {
        var rewards = await (
            from unlock in db.CommunityRewardUnlocks.AsNoTracking()
            join reward in db.CommunityRewardDefinitions.AsNoTracking()
                on unlock.RewardDefinitionId equals reward.Id
            where
                unlock.HostId == host.Id
                && unlock.ViewerTwitchUserId == passport.TwitchUserId
                && (reward.Kind == CommunityRewardKind.Title || reward.Kind == CommunityRewardKind.Badge)
            orderby reward.Name
            select new ViewerPassportRewardView(
                reward.Id,
                reward.Kind,
                reward.Name,
                reward.PresentationToken
            )
        ).ToArrayAsync(cancellationToken);
        var selectedTitle = rewards.SingleOrDefault(value =>
            value.Id == passport.SelectedTitleRewardDefinitionId
            && value.Kind == CommunityRewardKind.Title
        );
        var selectedBadge = rewards.SingleOrDefault(value =>
            value.Id == passport.SelectedBadgeRewardDefinitionId
            && value.Kind == CommunityRewardKind.Badge
        );
        return new ViewerPassportView(
            host.Id,
            host.Login,
            host.DisplayName,
            passport.TwitchUserId,
            passport.Login,
            passport.DisplayName,
            passport.ProfileLine,
            passport.Visibility,
            passport.HideAttendance,
            selectedTitle,
            selectedBadge,
            includeEarnedRewards
                ? rewards.Where(value => value.Kind == CommunityRewardKind.Title).ToArray()
                : [],
            includeEarnedRewards
                ? rewards.Where(value => value.Kind == CommunityRewardKind.Badge).ToArray()
                : [],
            await StatisticsAsync(db, passport, cancellationToken)
        );
    }

    private async Task<ViewerPassportStatistics> StatisticsAsync(
        BlokeBotDbContext db,
        ViewerPassport passport,
        CancellationToken cancellationToken
    )
    {
        var login = passport.Login;
        var pointBalance = string.IsNullOrWhiteSpace(login)
            ? new PointBalanceEntry(string.Empty, PointAmount.Zero, DateTime.MinValue)
            : await balances.GetBalanceAsync(passport.HostId, login, cancellationToken);
        var leaderboard = await balances.GetLeaderboardAsync(
            passport.HostId,
            int.MaxValue,
            cancellationToken
        );
        var rank = string.IsNullOrWhiteSpace(login)
            ? null
            : leaderboard
                .Select((value, index) => new { value.Login, Rank = index + 1 })
                .Where(value => value.Login == login)
                .Select(value => (int?)value.Rank)
                .SingleOrDefault();
        var guesses = string.IsNullOrWhiteSpace(login)
            ? []
            : await db
                .Votes.AsNoTracking()
                .Where(value =>
                    value.Login == login
                    && value.GuessRound != null
                    && value.GuessRound.HostId == passport.HostId
                    && value.GuessRound.Status == GuessRoundStatus.Completed
                )
                .Select(value => new { value.GuessName, value.GuessRound!.WinningName })
                .ToArrayAsync(cancellationToken);
        var correctGuesses = guesses.Count(value =>
            string.Equals(value.GuessName, value.WinningName, StringComparison.OrdinalIgnoreCase)
        );
        var giveawayWins = string.IsNullOrWhiteSpace(login)
            ? 0
            : await db.PointsGiveawayWinners.AsNoTracking().CountAsync(
                value =>
                    value.Login == login
                    && value.Giveaway != null
                    && value.Giveaway.HostId == passport.HostId,
                cancellationToken
            );
        var bounties = await db
            .BountyPledges.AsNoTracking()
            .Where(value =>
                value.HostId == passport.HostId
                && value.ContributorTwitchUserId == passport.TwitchUserId
            )
            .Select(value => value.BountyId)
            .Distinct()
            .CountAsync(cancellationToken);
        var moments = await db
            .MomentContributors.AsNoTracking()
            .Where(value =>
                value.TwitchUserId == passport.TwitchUserId
                && value.Candidate != null
                && value.Candidate.HostId == passport.HostId
                && value.Candidate.State == MomentCandidateState.Approved
            )
            .Select(value => value.CandidateId)
            .Distinct()
            .CountAsync(cancellationToken);
        var achievements = await (
            from completion in db.CommunityCompletions.AsNoTracking()
            join definition in db.CommunityDefinitions.AsNoTracking()
                on completion.DefinitionId equals definition.Id
            where
                completion.HostId == passport.HostId
                && completion.ViewerTwitchUserId == passport.TwitchUserId
                && definition.Kind == CommunityDefinitionKind.Achievement
            select completion.Id
        ).CountAsync(cancellationToken);
        var attendance = await db
            .ViewerPassportAttendanceDays.AsNoTracking()
            .Where(value => value.PassportId == passport.Id)
            .Select(value => value.DateUtc)
            .OrderByDescending(value => value)
            .ToArrayAsync(cancellationToken);
        return new ViewerPassportStatistics(
            pointBalance.Balance.ToDisplayString(),
            rank,
            guesses.Length,
            correctGuesses,
            AttendanceStreak(attendance),
            correctGuesses,
            giveawayWins,
            bounties,
            moments,
            achievements
        );
    }

    private static int AttendanceStreak(IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count == 0)
        {
            return 0;
        }
        var streak = 1;
        for (var index = 1; index < dates.Count; index++)
        {
            if (dates[index] != dates[index - 1].AddDays(-1))
            {
                break;
            }
            streak++;
        }
        return streak;
    }

    private static async Task<bool> CanViewAsync(
        BlokeBotDbContext db,
        ViewerPassport passport,
        ViewerPassportAudience audience,
        CancellationToken cancellationToken
    ) =>
        audience.IsChannelManager
        || audience.TwitchUserId == passport.TwitchUserId
        || passport.Visibility
            switch
            {
                ViewerPassportVisibility.Public => true,
                ViewerPassportVisibility.ChannelMembers
                    when audience.TwitchUserId is { Length: > 0 } id =>
                    await db.ViewerPassports.AsNoTracking().AnyAsync(
                        value => value.HostId == passport.HostId && value.TwitchUserId == id,
                        cancellationToken
                    ),
                _ => false,
            };

    private static async Task<bool> RewardsAreEarnedAsync(
        BlokeBotDbContext db,
        int hostId,
        string twitchUserId,
        long? titleId,
        long? badgeId,
        CancellationToken cancellationToken
    )
    {
        var selected = new[] { titleId, badgeId }.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (selected.Length == 0)
        {
            return true;
        }
        var earned = await (
            from unlock in db.CommunityRewardUnlocks.AsNoTracking()
            join reward in db.CommunityRewardDefinitions.AsNoTracking()
                on unlock.RewardDefinitionId equals reward.Id
            where
                unlock.HostId == hostId
                && unlock.ViewerTwitchUserId == twitchUserId
                && selected.Contains(reward.Id)
            select new { reward.Id, reward.Kind }
        ).ToArrayAsync(cancellationToken);
        return (titleId is null || earned.Any(value => value.Id == titleId && value.Kind == CommunityRewardKind.Title))
            && (badgeId is null || earned.Any(value => value.Id == badgeId && value.Kind == CommunityRewardKind.Badge));
    }

    private static async Task DetachReusedLoginAsync(
        BlokeBotDbContext db,
        int hostId,
        string twitchUserId,
        string login,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var stalePassports = await db
            .ViewerPassports.Where(value =>
                value.HostId == hostId
                && value.TwitchUserId != twitchUserId
                && value.Login == login
            )
            .ToArrayAsync(cancellationToken);
        foreach (var stale in stalePassports)
        {
            stale.Login = string.Empty;
            stale.UpdatedAtUtc = now;
        }
    }

    private static ViewerPassport DraftPassport(
        int hostId,
        ViewerPassportIdentity identity
    ) =>
        new()
        {
            HostId = hostId,
            TwitchUserId = identity.TwitchUserId,
            Login = identity.Login,
            DisplayName = identity.DisplayName,
            Visibility = ViewerPassportVisibility.Private,
            HideAttendance = true,
        };

    private static ViewerPassportIdentity? Normalize(ViewerPassportIdentity viewer)
    {
        var userId = viewer.TwitchUserId.Trim();
        var login = NormalizeLogin(viewer.Login);
        var displayName = viewer.DisplayName.Trim();
        return userId.Length is 0 or > 128 || login.Length is 0 or > 128 || displayName.Length > 160
            ? null
            : new(userId, login, displayName.Length == 0 ? login : displayName);
    }

    private static string? NormalizeProfileLine(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= ViewerPassportLimits.ProfileLineMaximumLength
            && normalized.All(character => !char.IsControl(character))
            ? normalized
            : null;
    }

    private static string NormalizeLogin(string value) => LoginName.Parse(value).Value;

    private SemaphoreSlim Gate(string twitchUserId) =>
        _mutationGates[(twitchUserId.GetHashCode(StringComparison.Ordinal) & int.MaxValue) % _mutationGates.Length];

    private static async Task<bool> HostExistsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) => await db.Hosts.AsNoTracking().AnyAsync(value => value.Id == hostId, cancellationToken);

    private static async Task<HostView?> EnabledHostAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Id == hostId
                && (value.EnabledFeatures & HostFeatureFlags.ViewerPassports)
                    == HostFeatureFlags.ViewerPassports
            )
            .Select(value => new HostView(
                value.Id,
                value.Login,
                value.DisplayName,
                value.EnabledFeatures
            ))
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record HostView(
        int Id,
        string Login,
        string DisplayName,
        HostFeatureFlags EnabledFeatures
    );
}
