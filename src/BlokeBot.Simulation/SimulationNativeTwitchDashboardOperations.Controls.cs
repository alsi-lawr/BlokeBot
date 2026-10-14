using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;

namespace BlokeBot.Simulation;

internal sealed partial class SimulationNativeTwitchDashboardOperations
{
    private bool _auditEnabled;

    internal void EnableAudit() => _auditEnabled = true;

    private int _auditPollReads;
    private int _auditPredictionReads;
    private int _auditPollVotes;

    internal SimulationDashboardProviderCounts AuditCounts() =>
        new(
            Volatile.Read(ref _auditPollReads),
            Volatile.Read(ref _auditPredictionReads),
            Volatile.Read(ref _auditPollVotes)
        );

    internal async Task<int?> AddPollVoteAsync(int hostId, CancellationToken ct)
    {
        if (_auditEnabled)
        {
            _ = Interlocked.Increment(ref _auditPollVotes);
        }
        int votes;
        lock (_gate)
        {
            var host = ForHost(hostId);
            if (host.ActivePoll is not { } poll)
            {
                return null;
            }
            var choices = poll.Choices.ToArray();
            choices[0] = choices[0] with { Votes = choices[0].Votes + 1 };
            votes = choices[0].Votes;
            host.ActivePoll = poll with { Choices = choices };
        }
        return await ChangedAsync(votes, ct);
    }

    internal void SetTemplates(
        int hostId,
        IReadOnlyList<PollTemplateView> polls,
        IReadOnlyList<PredictionTemplateView> predictions
    )
    {
        lock (_gate)
        {
            var host = ForHost(hostId);
            host.PollTemplates.Clear();
            host.PollTemplates.AddRange(polls);
            host.PredictionTemplates.Clear();
            host.PredictionTemplates.AddRange(predictions);
        }
    }

    Task<PollControlState> IPollDashboardOperations.ReadControlAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_auditEnabled)
        {
            _ = Interlocked.Increment(ref _auditPollReads);
        }
        lock (_gate)
        {
            var host = ForHost(hostId);
            return Task.FromResult(
                new PollControlState(
                    new PollAuthorizationReadiness.Ready(),
                    host.PollTemplates.SingleOrDefault(x => x.Id == templateId),
                    host.ActivePoll
                )
            );
        }
    }

    Task<PredictionControlState> IPredictionDashboardOperations.ReadControlAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_auditEnabled)
        {
            _ = Interlocked.Increment(ref _auditPredictionReads);
        }
        lock (_gate)
        {
            var host = ForHost(hostId);
            var active = host.ActivePrediction;
            return Task.FromResult(
                new PredictionControlState(
                    new PredictionAuthorizationReadiness.Ready(),
                    host.PredictionTemplates.SingleOrDefault(x => x.Id == templateId),
                    active is null
                        ? null
                        : new(
                            active.ProviderPredictionId,
                            active.Title,
                            active
                                .Outcomes.Select(x => new PredictionControlOutcomeView(
                                    x.Id,
                                    x.Title,
                                    x.Color,
                                    x.Users,
                                    x.ChannelPoints
                                ))
                                .ToArray(),
                            active.Status,
                            active.IsExternallyStarted,
                            active.CreatedAtUtc,
                            active.LocksAtUtc
                        )
                )
            );
        }
    }
}
