using System.Diagnostics;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
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

public sealed record ViewerCommandCatalogEntry(
    string Name,
    ViewerCommandCatalogSource Source,
    string? AccessSummary = null
)
{
    internal ViewerCommandCatalogAvailability Availability { get; init; }
}

internal enum ViewerCommandCatalogAvailability
{
    Available,
    TurnedOff,
    ActionUnavailable,
    Shadowed,
}

public sealed record ViewerCommandCatalogSnapshot(
    IReadOnlyList<ViewerCommandCatalogEntry> Entries,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> UnavailableFeatures
)
{
    public IReadOnlyList<string> Names => Entries.Select(static entry => entry.Name).ToArray();
}

public sealed class ViewerCommandCatalogService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostStreamLivenessProvider streams,
    IOverlayCueAdmissionService overlayCues,
    ICustomCommandAutomationRuntime automations
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
                null,
                ct
            );
    }

    public async Task<ViewerCommandCatalogSnapshot> LoadForViewerAsync(
        string channelLogin,
        ChatMessage viewer,
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
                viewer,
                ct
            );
    }

    private async Task<ViewerCommandCatalogSnapshot> LoadAsync(
        BlokeBotDbContext db,
        int hostId,
        string hostLogin,
        HostFeatureFlags enabledFeatures,
        string? defaultConflictAlias,
        ChatMessage? viewer,
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
        var boards = enabledFeatures.Contains(HostFeatureFlags.RequestBoards)
            ? await db
                .RequestBoards.AsNoTracking()
                .Where(value => value.HostId == hostId)
                .Select(value => new { value.IsOpen, value.VotingEnabled })
                .ToArrayAsync(ct)
            : [];
        var publicBounties = enabledFeatures.Contains(
            HostFeatureFlags.Bounties | HostFeatureFlags.Points
        )
            ? await db
                .Bounties.AsNoTracking()
                .Where(value =>
                    value.HostId == hostId && value.Visibility == BountyVisibility.Public
                )
                .Select(value => value.Status)
                .ToArrayAsync(ct)
            : [];
        var bingoStatus = enabledFeatures.Contains(HostFeatureFlags.Bingo)
            ? await db
                .BingoGames.AsNoTracking()
                .Where(value =>
                    value.HostId == hostId
                    && (
                        value.Status == BingoGameStatus.Joining
                        || value.Status == BingoGameStatus.Issued
                    )
                )
                .Select(value => (BingoGameStatus?)value.Status)
                .SingleOrDefaultAsync(ct)
            : null;
        var queues = enabledFeatures.Contains(HostFeatureFlags.PlayWithViewers)
            ? await db
                .PlayQueues.AsNoTracking()
                .Where(value => value.HostId == hostId)
                .Select(value => value.IsOpen)
                .ToArrayAsync(ct)
            : [];
        var customCommands =
            viewer is null || enabledFeatures.Contains(HostFeatureFlags.CustomCommands)
                ? await db
                    .CustomCommands.AsNoTracking()
                    .AsSplitQuery()
                    .Include(value => value.Action)
                    .Include(value => value.Aliases)
                    .Include(value => value.AllowedUsers)
                    .Where(value => value.HostId == hostId)
                    .ToArrayAsync(ct)
                : [];
        var availableCueActions = enabledFeatures.Contains(
            HostFeatureFlags.CustomCommands | HostFeatureFlags.Overlays
        )
            ? await AvailableCueActionsAsync(hostId, customCommands, ct)
            : new HashSet<CueActionIdentity>();
        var availableAutomationCommands = enabledFeatures.Contains(
            HostFeatureFlags.CustomCommands | HostFeatureFlags.Automations
        )
            ? await automations.AvailableCommandIdsAsync(new(hostId), ct)
            : new HashSet<int>();

        HostStreamLivenessOutcome liveness = new HostStreamLivenessOutcome.Offline();
        if (enabledFeatures.Contains(HostFeatureFlags.Moments))
        {
            var livenessResult = await streams.GetStreamLiveness(hostLogin).ExecuteAsync(ct);
            liveness = livenessResult.Match(value => value, _ => throw new UnreachableException());
        }
        var conflicts = new List<string>();
        if (!string.IsNullOrWhiteSpace(defaultConflictAlias))
        {
            conflicts.Add(
                $"Default !{defaultConflictAlias} conflicts with another command. Choose a different Commands alias."
            );
        }

        var unavailable = new List<string>();
        if (
            enabledFeatures.Contains(HostFeatureFlags.Moments)
            && liveness is HostStreamLivenessOutcome.Unavailable
        )
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

        if (enabledFeatures.Contains(HostFeatureFlags.RequestBoards) && boards.Length > 0)
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Requests));
        }
        if (
            enabledFeatures.Contains(HostFeatureFlags.RequestBoards)
            && boards.Any(value => value.IsOpen)
        )
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Request));
        }
        if (
            enabledFeatures.Contains(HostFeatureFlags.RequestBoards)
            && boards.Any(value => value.VotingEnabled)
        )
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.RequestVote));
        }

        if (enabledFeatures.Contains(HostFeatureFlags.Bounties | HostFeatureFlags.Points))
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Bounties));
            if (publicBounties.Length > 0)
            {
                candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Bounty));
            }
            if (publicBounties.Contains(BountyStatus.Funding))
            {
                candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.BountyPledge));
            }
        }

        if (enabledFeatures.Contains(HostFeatureFlags.CommunityProgression))
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Progress));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.EquipTitle));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.EquipBadge));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.EquipAccent));
        }

        if (enabledFeatures.Contains(HostFeatureFlags.ViewerPassports))
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Passport));
        }

        if (enabledFeatures.Contains(HostFeatureFlags.Bingo) && bingoStatus is not null)
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Bingo));
            if (bingoStatus == BingoGameStatus.Joining)
            {
                candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.BingoJoin));
                candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.BingoLeave));
            }
        }

        if (enabledFeatures.Contains(HostFeatureFlags.PlayWithViewers) && queues.Length > 0)
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Queue));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Leave));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Position));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Ready));
        }
        if (
            enabledFeatures.Contains(HostFeatureFlags.PlayWithViewers) && queues.Any(value => value)
        )
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Join));
        }

        if (
            enabledFeatures.Contains(HostFeatureFlags.Moments)
            && liveness is HostStreamLivenessOutcome.Live
        )
        {
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Moment));
            candidates.Add(Candidate.Fixed(FixedChatCommandRoutes.Clip));
        }

        if (viewer is null || enabledFeatures.Contains(HostFeatureFlags.CustomCommands))
        {
            foreach (var command in customCommands)
            {
                var canonical = command
                    .Aliases.OrderBy(value => value.SortOrder)
                    .ThenBy(value => value.Id)
                    .Select(value => value.Alias)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(canonical))
                {
                    continue;
                }

                var availability = CustomAvailability(
                    command,
                    enabledFeatures,
                    availableCueActions,
                    availableAutomationCommands
                );
                if (
                    viewer is not null
                    && (
                        availability is not ViewerCommandCatalogAvailability.Available
                        || !CustomCommandAccessPolicy.Allows(hostLogin, command, viewer)
                    )
                )
                {
                    continue;
                }

                candidates.Add(
                    Candidate.Custom(
                        canonical,
                        command.Id,
                        CustomAccessSummary(command),
                        availability
                    )
                );
            }
        }

        var inventoryForOwner = viewer is null;
        var listedCandidates = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            if (inventoryForOwner && candidate.Source == ViewerCommandCatalogSource.Custom)
            {
                var ownerCandidate = candidate;
                if (
                    candidate.Availability is ViewerCommandCatalogAvailability.Available
                    && !IsActuallyDispatchedTo(
                        candidate,
                        appAliases,
                        customCommands,
                        enabledFeatures,
                        availableCueActions,
                        availableAutomationCommands
                    )
                )
                {
                    AddConflict(conflicts, candidate, listed: true);
                    ownerCandidate = candidate with
                    {
                        Availability = ViewerCommandCatalogAvailability.Shadowed,
                    };
                }

                listedCandidates.Add(ownerCandidate);
                continue;
            }

            if (
                IsActuallyDispatchedTo(
                    candidate,
                    appAliases,
                    customCommands,
                    enabledFeatures,
                    availableCueActions,
                    availableAutomationCommands
                )
            )
            {
                listedCandidates.Add(candidate);
            }
            else
            {
                AddConflict(conflicts, candidate, listed: false);
            }
        }

        var entries = listedCandidates
            .DistinctBy(candidate => candidate.LogicalIdentity)
            .Select(candidate => new ViewerCommandCatalogEntry(
                $"!{candidate.Alias}",
                candidate.Source,
                candidate.AccessSummary
            )
            {
                Availability = candidate.Availability,
            })
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        return new(entries, conflicts.Distinct(StringComparer.Ordinal).ToArray(), unavailable);
    }

    private static void AddConflict(
        ICollection<string> conflicts,
        Candidate candidate,
        bool listed
    ) =>
        conflicts.Add(
            listed
                ? $"!{candidate.Alias} is shadowed by another command. Change that alias to make it available."
                : $"!{candidate.Alias} is shadowed by another command and is not listed. Change that alias to make it available."
        );

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
        HostFeatureFlags enabledFeatures,
        IReadOnlySet<CueActionIdentity> availableCueActions,
        IReadOnlySet<int> availableAutomationCommands
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
        return customOwner is not null
            && IsCustomActionAvailable(
                customOwner.Command,
                enabledFeatures,
                availableCueActions,
                availableAutomationCommands
            )
            && candidate.CustomCommandId == customOwner.Command.Id;
    }

    private static bool IsCustomActionAvailable(
        CustomCommand command,
        HostFeatureFlags enabledFeatures,
        IReadOnlySet<CueActionIdentity> availableCueActions,
        IReadOnlySet<int> availableAutomationCommands
    ) =>
        command.Action switch
        {
            OverlayCueCustomCommandAction cue => enabledFeatures.Contains(
                HostFeatureFlags.CustomCommands | HostFeatureFlags.Overlays
            ) && availableCueActions.Contains(new(cue.TargetOverlayPublicId, cue.CuePublicId)),
            AutomationCustomCommandAction => enabledFeatures.Contains(
                HostFeatureFlags.CustomCommands | HostFeatureFlags.Automations
            ) && availableAutomationCommands.Contains(command.Id),
            _ => enabledFeatures.Contains(HostFeatureFlags.CustomCommands),
        };

    private static string CustomAccessSummary(CustomCommand command) =>
        CustomCommandAccessPolicy.Describe(
            command.AllowEveryone,
            command.AllowModerators,
            command.AllowedUsers.Count
        );

    private static ViewerCommandCatalogAvailability CustomAvailability(
        CustomCommand command,
        HostFeatureFlags enabledFeatures,
        IReadOnlySet<CueActionIdentity> availableCueActions,
        IReadOnlySet<int> availableAutomationCommands
    ) =>
        command.Enabled && enabledFeatures.Contains(HostFeatureFlags.CustomCommands)
            ? IsCustomActionAvailable(
                command,
                enabledFeatures,
                availableCueActions,
                availableAutomationCommands
            )
                ? ViewerCommandCatalogAvailability.Available
                : ViewerCommandCatalogAvailability.ActionUnavailable
            : ViewerCommandCatalogAvailability.TurnedOff;

    private async Task<IReadOnlySet<CueActionIdentity>> AvailableCueActionsAsync(
        int hostId,
        IReadOnlyList<CustomCommand> commands,
        CancellationToken ct
    )
    {
        var identities = commands
            .Select(command => command.Action)
            .OfType<OverlayCueCustomCommandAction>()
            .Select(action => new CueActionIdentity(
                action.TargetOverlayPublicId,
                action.CuePublicId
            ))
            .Distinct()
            .ToArray();
        if (identities.Length == 0)
        {
            return new HashSet<CueActionIdentity>();
        }

        var resolutions = await Task.WhenAll(
            identities.Select(async identity =>
                (
                    Identity: identity,
                    Outcome: await overlayCues.ResolveReferencesAsync(
                        new(hostId, identity.TargetOverlayId, identity.CueId),
                        ct
                    )
                )
            )
        );
        return resolutions
            .Where(value => value.Outcome is OverlayCueReferenceOutcome.Available)
            .Select(value => value.Identity)
            .ToHashSet();
    }

    private static bool IsAppRouteEnabled(AppCommandKind kind, HostFeatureFlags enabledFeatures) =>
        kind switch
        {
            AppCommandKind.Commands => true,
            AppCommandKind.Start
            or AppCommandKind.Stop
            or AppCommandKind.Win
            or AppCommandKind.Guess
            or AppCommandKind.Guesses => enabledFeatures.Contains(HostFeatureFlags.Guessing),
            _ => enabledFeatures.Contains(HostFeatureFlags.Points),
        };

    private sealed record AppAlias(AppCommandKind Kind, int? ProfileId, string Alias);

    private readonly record struct CueActionIdentity(Guid TargetOverlayId, Guid CueId);

    private sealed record Candidate(
        string Alias,
        string LogicalIdentity,
        ViewerCommandCatalogSource Source,
        bool FixedRoute,
        AppCommandKind? AppKind,
        int? CustomCommandId,
        string? AccessSummary,
        ViewerCommandCatalogAvailability Availability
    )
    {
        public static Candidate Fixed(FixedChatCommandRoute route) =>
            new(
                route.Value,
                $"fixed:{route.Value}",
                ViewerCommandCatalogSource.BuiltIn,
                true,
                null,
                null,
                null,
                ViewerCommandCatalogAvailability.Available
            );

        public static Candidate App(string alias, AppCommandKind kind) =>
            new(
                alias,
                $"app:{kind}",
                ViewerCommandCatalogSource.BuiltIn,
                false,
                kind,
                null,
                null,
                ViewerCommandCatalogAvailability.Available
            );

        public static Candidate Custom(
            string alias,
            int commandId,
            string accessSummary,
            ViewerCommandCatalogAvailability availability
        ) =>
            new(
                alias,
                $"custom:{commandId}",
                ViewerCommandCatalogSource.Custom,
                false,
                null,
                commandId,
                accessSummary,
                availability
            );
    }
}
