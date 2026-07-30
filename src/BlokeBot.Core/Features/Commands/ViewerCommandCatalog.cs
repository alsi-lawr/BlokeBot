using System.Diagnostics;
using BlokeBot.Commands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Commands;

public enum ViewerCommandCatalogSource
{
    BuiltIn,
    Custom,
}

public sealed record ViewerCommandCatalogEntry(string Name, ViewerCommandCatalogSource Source);

public sealed record ViewerCommandCatalogSnapshot(
    IReadOnlyList<ViewerCommandCatalogEntry> Entries,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> UnavailableFeatures
)
{
    public IReadOnlyList<string> Names => Entries.Select(entry => entry.Name).ToArray();
}

public sealed class ViewerCommandCatalogService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostStreamLivenessProvider streams
)
{
    public async Task<ViewerCommandCatalogSnapshot> LoadForHostAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId)
            .Select(value => new
            {
                value.Id,
                value.Login,
                value.EnabledFeatures,
                value.CommandsDefaultConflictAlias,
            })
            .SingleOrDefaultAsync(ct);
        return host is null
            ? new ViewerCommandCatalogSnapshot([], [], ["Channel setup is unavailable."])
            : await LoadAsync(
                db,
                host.Id,
                host.Login,
                host.EnabledFeatures,
                host.CommandsDefaultConflictAlias,
                ct
            );
    }

    public async Task<ViewerCommandCatalogSnapshot> LoadForChannelAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var normalizedLogin = LoginName.Parse(channelLogin).Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Login == normalizedLogin)
            .Select(value => new
            {
                value.Id,
                value.Login,
                value.EnabledFeatures,
                value.CommandsDefaultConflictAlias,
            })
            .SingleOrDefaultAsync(ct);
        return host is null
            ? new ViewerCommandCatalogSnapshot([], [], ["Channel setup is unavailable."])
            : await LoadAsync(
                db,
                host.Id,
                host.Login,
                host.EnabledFeatures,
                host.CommandsDefaultConflictAlias,
                ct
            );
    }

    private async Task<ViewerCommandCatalogSnapshot> LoadAsync(
        BlokeBotDbContext db,
        int hostId,
        string hostLogin,
        HostFeatureFlags enabledFeatures,
        string? defaultConflictAlias,
        CancellationToken ct
    )
    {
        var appAliases = await db
            .CommandAliases.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new AppAlias(value.Kind, value.GuessRoundProfileId, value.Alias))
            .ToArrayAsync(ct);
        var openProfileId = await db
            .Rounds.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Status == GuessRoundStatus.Open)
            .OrderByDescending(value => value.StartedAtUtc)
            .Select(value => (int?)value.GuessRoundProfileId)
            .FirstOrDefaultAsync(ct);
        var activeGiveaway = await db
            .PointsGiveaways.AsNoTracking()
            .AnyAsync(
                value => value.HostId == hostId && value.Status == PointsGiveawayStatus.Active,
                ct
            );
        var boards = await db
            .RequestBoards.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new { value.IsOpen, value.VotingEnabled })
            .ToArrayAsync(ct);
        var queues = await db
            .PlayQueues.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => value.IsOpen)
            .ToArrayAsync(ct);
        var customCommands = await db
            .CustomCommands.AsNoTracking()
            .Include(value => value.Aliases)
            .Where(value => value.HostId == hostId)
            .ToArrayAsync(ct);

        var livenessResult = await streams.GetStreamLiveness(hostLogin).ExecuteAsync(ct);
        var liveness = livenessResult.Match(value => value, _ => throw new UnreachableException());
        var conflicts = new List<string>();
        if (!string.IsNullOrWhiteSpace(defaultConflictAlias))
        {
            conflicts.Add(
                $"Default !{defaultConflictAlias} conflicts with another command. Choose a different Commands alias."
            );
        }

        var unavailable = new List<string>();
        if (liveness is HostStreamLivenessOutcome.Unavailable)
        {
            unavailable.Add(
                "Moment commands are unavailable while Twitch stream identity is unavailable."
            );
        }

        var candidates = new List<Candidate>();
        AddAppCandidate(candidates, appAliases, AppCommandKind.Commands, null);
        if (openProfileId is { } profileId && enabledFeatures.Contains(HostFeatureFlags.Guessing))
        {
            AddAppCandidate(candidates, appAliases, AppCommandKind.Guess, profileId);
            AddAppCandidate(candidates, appAliases, AppCommandKind.Guesses, profileId);
        }

        if (enabledFeatures.Contains(HostFeatureFlags.Points))
        {
            AddAppCandidate(candidates, appAliases, AppCommandKind.Points, null);
            AddAppCandidate(candidates, appAliases, AppCommandKind.GivePoints, null);
            AddAppCandidate(candidates, appAliases, AppCommandKind.Gamble, null);
            if (activeGiveaway)
            {
                AddAppCandidate(candidates, appAliases, AppCommandKind.Join, null);
            }
        }

        if (boards.Length > 0)
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Requests));
        }
        if (boards.Any(value => value.IsOpen))
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Request));
        }
        if (boards.Any(value => value.VotingEnabled))
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.RequestVote));
        }

        if (queues.Length > 0)
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Queue));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Leave));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Position));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Ready));
        }
        if (queues.Any(value => value))
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Join));
        }

        if (liveness is HostStreamLivenessOutcome.Live)
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Moment));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Clip));
        }

        if (enabledFeatures.Contains(HostFeatureFlags.CustomCommands))
        {
            foreach (
                var command in customCommands.Where(value => value.Enabled && !value.ModeratorOnly)
            )
            {
                var canonical = command
                    .Aliases.OrderBy(value => value.SortOrder)
                    .ThenBy(value => value.Id)
                    .Select(value => value.Alias)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(canonical))
                {
                    candidates.Add(Candidate.Custom(canonical, command.Id));
                }
            }
        }

        var entries = candidates
            .Where(candidate =>
                IsActuallyDispatchedTo(candidate, appAliases, customCommands, enabledFeatures)
                || AddConflict(conflicts, candidate)
            )
            .DistinctBy(candidate => candidate.LogicalIdentity)
            .Select(candidate => new ViewerCommandCatalogEntry(
                $"!{candidate.Alias}",
                candidate.Source
            ))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        return new(entries, conflicts.Distinct(StringComparer.Ordinal).ToArray(), unavailable);
    }

    private static bool AddConflict(ICollection<string> conflicts, Candidate candidate)
    {
        conflicts.Add(
            $"!{candidate.Alias} is shadowed by another command and is not listed. Change that alias to make it available."
        );
        return false;
    }

    private static void AddAppCandidate(
        ICollection<Candidate> candidates,
        IEnumerable<AppAlias> aliases,
        AppCommandKind kind,
        int? profileId
    )
    {
        var canonical = aliases
            .Where(value => value.Kind == kind && value.ProfileId == profileId)
            .Select(value => value.Alias)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (canonical is not null)
        {
            candidates.Add(Candidate.App(canonical, kind));
        }
    }

    private static bool IsActuallyDispatchedTo(
        Candidate candidate,
        IReadOnlyList<AppAlias> appAliases,
        IReadOnlyList<CustomCommand> customCommands,
        HostFeatureFlags enabledFeatures
    )
    {
        if (candidate.FixedRoute)
        {
            return true;
        }
        if (FixedChatCommandRoutes.All.Contains(candidate.Alias))
        {
            return false;
        }

        var appOwner = appAliases.FirstOrDefault(value =>
            string.Equals(value.Alias, candidate.Alias, StringComparison.OrdinalIgnoreCase)
            && IsAppRouteEnabled(value.Kind, enabledFeatures)
        );
        if (appOwner is not null)
        {
            return candidate.AppKind == appOwner.Kind;
        }

        if (!enabledFeatures.Contains(HostFeatureFlags.CustomCommands))
        {
            return false;
        }

        var customOwner = customCommands
            .SelectMany(command =>
                command.Aliases.Select(alias => new { Command = command, Alias = alias })
            )
            .FirstOrDefault(value =>
                string.Equals(
                    value.Alias.Alias,
                    candidate.Alias,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        return customOwner is not null && candidate.CustomCommandId == customOwner.Command.Id;
    }

    private static bool IsAppRouteEnabled(AppCommandKind kind, HostFeatureFlags enabledFeatures)
    {
        return kind switch
        {
            AppCommandKind.Commands => true,
            AppCommandKind.Start
            or AppCommandKind.Stop
            or AppCommandKind.Win
            or AppCommandKind.Guess
            or AppCommandKind.Guesses => enabledFeatures.Contains(HostFeatureFlags.Guessing),
            _ => enabledFeatures.Contains(HostFeatureFlags.Points),
        };
    }

    private sealed record AppAlias(AppCommandKind Kind, int? ProfileId, string Alias);

    private sealed record Candidate(
        string Alias,
        string LogicalIdentity,
        ViewerCommandCatalogSource Source,
        bool FixedRoute,
        AppCommandKind? AppKind,
        int? CustomCommandId
    )
    {
        public static Candidate Fixed(FixedChatCommandRoute route)
        {
            return new(
                route.Value,
                $"fixed:{route.Value}",
                ViewerCommandCatalogSource.BuiltIn,
                true,
                null,
                null
            );
        }

        public static Candidate App(string alias, AppCommandKind kind)
        {
            return new(alias, $"app:{kind}", ViewerCommandCatalogSource.BuiltIn, false, kind, null);
        }

        public static Candidate Custom(string alias, int commandId)
        {
            return new(
                alias,
                $"custom:{commandId}",
                ViewerCommandCatalogSource.Custom,
                false,
                null,
                commandId
            );
        }
    }
}
