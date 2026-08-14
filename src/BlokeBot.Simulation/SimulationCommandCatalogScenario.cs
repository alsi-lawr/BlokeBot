using BlokeBot.Commands;
using BlokeBot.Core;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Simulation;

internal enum SimulationStreamLiveness
{
    Production,
    Live,
    Offline,
    Unavailable,
}

internal sealed class SimulationCommandCatalogScenario(
    HostBotStatusService productionStreams,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events,
    PointsGiveawayChangeNotifier giveawayChanges,
    HostFeatureService hostFeatures
) : IHostStreamLivenessProvider
{
    private SimulationStreamLiveness _liveness = SimulationStreamLiveness.Production;

    public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
        _liveness switch
        {
            SimulationStreamLiveness.Production => productionStreams.GetStreamLiveness(
                channelLogin
            ),
            SimulationStreamLiveness.Live => Outcome(
                new HostStreamLivenessOutcome.Live(
                    "simulation-stream",
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                )
            ),
            SimulationStreamLiveness.Offline => Outcome(new HostStreamLivenessOutcome.Offline()),
            SimulationStreamLiveness.Unavailable => Outcome(
                new HostStreamLivenessOutcome.Unavailable(
                    HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                    new InvalidOperationException(
                        "Simulation requested unavailable stream identity."
                    )
                )
            ),
            _ => throw new ArgumentOutOfRangeException(),
        };

    public async Task SetLivenessAsync(string state, CancellationToken ct)
    {
        _liveness = state.ToLowerInvariant() switch
        {
            "production" => SimulationStreamLiveness.Production,
            "live" => SimulationStreamLiveness.Live,
            "offline" => SimulationStreamLiveness.Offline,
            "unavailable" => SimulationStreamLiveness.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown liveness."),
        };
        _ = await events.PublishAsync(AppEventKind.MomentsChanged, ct);
    }

    public async Task SetRoundAsync(string state, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await HostIdAsync(db, ct);
        var round = await db
            .Rounds.Where(value =>
                value.HostId == hostId
                && (
                    value.Status == GuessRoundStatus.Open || value.Status == GuessRoundStatus.Closed
                )
            )
            .OrderByDescending(value => value.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        round ??= await db
            .Rounds.Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.StartedAtUtc)
            .FirstAsync(ct);

        switch (state.ToLowerInvariant())
        {
            case "open":
                round.Status = GuessRoundStatus.Open;
                round.ClosedAtUtc = null;
                round.WinningName = null;
                break;
            case "closed":
                round.Status = GuessRoundStatus.Closed;
                round.ClosedAtUtc = SimulationMode.Now.UtcDateTime;
                round.WinningName = null;
                break;
            case "none":
                round.Status = GuessRoundStatus.Completed;
                round.ClosedAtUtc = SimulationMode.Now.UtcDateTime;
                round.WinningName = "Blue";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown round state.");
        }

        _ = await db.SaveChangesAsync(ct);
        _ = await events.PublishAsync(AppEventKind.GuessingChanged, ct);
    }

    public async Task SetGiveawayAsync(string state, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await HostIdAsync(db, ct);
        var giveaway = await db
            .PointsGiveaways.Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.StartedAtUtc)
            .FirstAsync(ct);
        switch (state.ToLowerInvariant())
        {
            case "active":
            case "open":
                giveaway.Status = PointsGiveawayStatus.Active;
                giveaway.EndsAtUtc = SimulationMode.Now.UtcDateTime.AddMinutes(5);
                giveaway.CompletedAtUtc = null;
                break;
            case "ending":
                giveaway.Status = PointsGiveawayStatus.Active;
                giveaway.EndsAtUtc = SimulationMode.Now.UtcDateTime.AddSeconds(-1);
                giveaway.CompletedAtUtc = null;
                break;
            case "inactive":
            case "completed":
                giveaway.Status = PointsGiveawayStatus.Completed;
                giveaway.CompletedAtUtc = SimulationMode.Now.UtcDateTime;
                break;
            case "cancelled":
                giveaway.Status = PointsGiveawayStatus.Cancelled;
                giveaway.CompletedAtUtc = SimulationMode.Now.UtcDateTime;
                break;
            case "expired":
                giveaway.Status = PointsGiveawayStatus.Expired;
                giveaway.CompletedAtUtc = SimulationMode.Now.UtcDateTime;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(state),
                    state,
                    "Unknown giveaway state."
                );
        }

        if (
            giveaway.Status is PointsGiveawayStatus.Completed
            && !await db.PointsGiveawayWinners.AnyAsync(
                value => value.GiveawayId == giveaway.Id,
                ct
            )
        )
        {
            db.PointsGiveawayWinners.AddRange(
                new PointsGiveawayWinner
                {
                    GiveawayId = giveaway.Id,
                    Login = "nightowl",
                    Payout = "500",
                },
                new PointsGiveawayWinner
                {
                    GiveawayId = giveaway.Id,
                    Login = "newviewer",
                    Payout = "250",
                }
            );
        }

        _ = await db.SaveChangesAsync(ct);
        await giveawayChanges.NotifyChangedAsync(hostId, ct);
    }

    public async Task SetFeatureAvailabilityAsync(string state, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await HostIdAsync(db, ct);
        var enabledFeatures = state.ToLowerInvariant() switch
        {
            "available" => HostFeatureFlags.All,
            "all-enabled" => HostFeatureFlags.All,
            "all-disabled" => HostFeatureFlags.None,
            "selective-native" => HostFeatureFlags.RaidCollaboration | HostFeatureFlags.Predictions,
            "mixed" => HostFeatureFlags.RequestBoards
                | HostFeatureFlags.Moments
                | HostFeatureFlags.Points
                | HostFeatureFlags.CustomCommands,
            "unavailable" => HostFeatureFlags.NativeTwitchFeatures
                | HostFeatureFlags.Moments
                | HostFeatureFlags.Overlays,
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown feature state."
            ),
        };
        foreach (var feature in HostFeatureCatalog.Features)
        {
            if (enabledFeatures.Contains(feature))
            {
                await hostFeatures.EnableAsync(hostId, feature, ct);
            }
            else
            {
                await hostFeatures.DisableAsync(hostId, feature, ct);
            }
        }
        _ = await events.PublishAsync(AppEventKind.CommandsChanged, ct);
    }

    public async Task SetAlertsAsync(string state, DurableAlertService alerts, CancellationToken ct)
    {
        if (!string.Equals(state, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown alerts state.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await HostIdAsync(db, ct);
        _ = await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Critical,
                "twitch-outbound-queue",
                "simulation-outbound-backlog",
                "Chat messages are backing up",
                "The bot's outgoing chat queue is not draining. Viewers may not be seeing replies. Check the bot's connection for this channel.",
                "/host#bot-status"
            )
            .RunAsync(ct);
        _ = await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "follower-only-chat",
                "simulation-follower-only",
                "Follower-only chat may block the bot",
                "This channel turned on follower-only chat and the bot account does not qualify yet. Replies may be rejected until it follows or is exempt.",
                "/host#bot-status"
            )
            .RunAsync(ct);
    }

    public async Task<ViewerCommandCatalogSnapshot> SnapshotAsync(
        ViewerCommandCatalogService catalog,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await catalog.LoadForHostAsync(await HostIdAsync(db, ct), ct);
    }

    public async Task<IReadOnlyList<string>> DispatchAsync(
        ChatCommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var responses = new List<string>();
        await dispatcher.DispatchResponsesAsync(
            new ChatMessage(
                "simulationviewer",
                FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login,
                "!commands",
                "simulation-command-catalog",
                new Dictionary<string, string>()
            ),
            (response, _) =>
            {
                responses.Add(response.Message);
                return ValueTask.CompletedTask;
            },
            ct
        );
        return responses;
    }

    private static IO<HostStreamLivenessOutcome, Never> Outcome(
        HostStreamLivenessOutcome outcome
    ) =>
        IO<HostStreamLivenessOutcome, Never>.Create(_ =>
            ValueTask.FromResult(Result<HostStreamLivenessOutcome, Never>.Success(outcome))
        );

    private static Task<int> HostIdAsync(BlokeBotDbContext db, CancellationToken ct)
    {
        var login = FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login;
        return db
            .Hosts.Where(value => value.Login == login)
            .Select(value => value.Id)
            .SingleAsync(ct);
    }
}
