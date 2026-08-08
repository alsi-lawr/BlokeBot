using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;

namespace BlokeBot.Core.Features.TwitchOperations;

/// <summary>
/// The poll operations automation actions invoke. Implementations reuse the native application
/// service's feature gate, readiness checks, host isolation, and typed outcomes; automations never
/// call Twitch poll endpoints through any other path.
/// </summary>
public interface IPollAutomationOperations
{
    Task<PollOperationOutcome> StartAsync(
        int hostId,
        PollTemplateDraft draft,
        CancellationToken cancellationToken
    );

    Task<PollOperationOutcome> EndAsync(
        int hostId,
        bool confirmedExternal,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// The prediction operations automation actions invoke. Implementations reuse the native
/// application service's feature gate, readiness checks, host isolation, and typed outcomes;
/// automations never call Twitch prediction endpoints through any other path.
/// </summary>
public interface IPredictionAutomationOperations
{
    Task<PredictionOperationOutcome> StartAsync(
        int hostId,
        PredictionTemplateDraft draft,
        CancellationToken cancellationToken
    );

    Task<PredictionOperationOutcome> LockAsync(
        int hostId,
        bool confirmed,
        CancellationToken cancellationToken
    );

    Task<PredictionOperationOutcome> CancelAsync(
        int hostId,
        bool confirmed,
        CancellationToken cancellationToken
    );

    Task<PredictionOperationOutcome> ResolveAsync(
        int hostId,
        string winningOutcomeId,
        bool confirmed,
        CancellationToken cancellationToken
    );
}
