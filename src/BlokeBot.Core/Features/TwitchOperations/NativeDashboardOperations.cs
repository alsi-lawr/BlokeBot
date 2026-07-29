using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;

namespace BlokeBot.Core.Features.TwitchOperations;

public interface IShoutoutDashboardOperations
{
    Task<ShoutoutDashboardState> LoadAsync(
        int hostId,
        string? targetLogin,
        CancellationToken cancellationToken
    );

    Task<ShoutoutOperationOutcome> SendAsync(
        int hostId,
        string targetLogin,
        CancellationToken cancellationToken
    );
}

public interface IPollDashboardOperations
{
    Task<PollDashboardState> LoadAsync(int hostId, CancellationToken cancellationToken);

    Task<PollOperationOutcome> SaveTemplateAsync(
        int hostId,
        PollTemplateDraft draft,
        CancellationToken cancellationToken
    );

    Task<PollOperationOutcome> StartAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    );

    Task<PollOperationOutcome> EndAsync(
        int hostId,
        bool confirmedExternal,
        CancellationToken cancellationToken
    );
}

public interface IClipMarkerDashboardOperations
{
    Task<ClipMarkerDashboardState> LoadAsync(int hostId, CancellationToken cancellationToken);

    Task<ClipMarkerOperationOutcome> CreateClipAsync(
        int hostId,
        bool hasDelay,
        CancellationToken cancellationToken
    );

    Task<ClipMarkerOperationOutcome> CreateMarkerAsync(
        int hostId,
        string description,
        CancellationToken cancellationToken
    );

    Task<ClipMarkerOperationOutcome> RetryClipAsync(
        int hostId,
        ClipAttemptReference attempt,
        CancellationToken cancellationToken
    );

    Task<ClipMarkerOperationOutcome> RetryMarkerAsync(
        int hostId,
        StreamMarkerAttemptReference attempt,
        CancellationToken cancellationToken
    );
}

public interface IChannelPointsDashboardOperations
{
    Task<ChannelPointsDashboardState> LoadAsync(int hostId, CancellationToken cancellationToken);

    Task<ChannelPointsOperationOutcome> CreateRewardAsync(
        int hostId,
        ChannelPointsRewardDraft draft,
        CancellationToken cancellationToken
    );

    Task<ChannelPointsOperationOutcome> UpdateRewardAsync(
        int hostId,
        string rewardId,
        ChannelPointsRewardDraft draft,
        bool isEnabled,
        bool paused,
        CancellationToken cancellationToken
    );

    Task<ChannelPointsOperationOutcome> DeleteRewardAsync(
        int hostId,
        string rewardId,
        bool confirmed,
        CancellationToken cancellationToken
    );

    Task<ChannelPointsOperationOutcome> UpdateRedemptionAsync(
        int hostId,
        string redemptionId,
        bool fulfill,
        CancellationToken cancellationToken
    );
}

public interface IPredictionDashboardOperations
{
    Task<PredictionDashboardState> LoadAsync(int hostId, CancellationToken cancellationToken);

    Task<PredictionOperationOutcome> SaveTemplateAsync(
        int hostId,
        PredictionTemplateDraft draft,
        CancellationToken cancellationToken
    );

    Task<PredictionOperationOutcome> DeleteTemplateAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    );

    Task<PredictionOperationOutcome> StartAsync(
        int hostId,
        int templateId,
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
