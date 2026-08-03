using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Moments;

public partial class MomentsPage
{
    private MomentModeratorPage? _page;
    private readonly Dictionary<Guid, MomentDraft> _drafts = [];
    private string _mergeWindow = MomentLimits.DefaultMergeWindowSeconds.ToString(
        CultureInfo.InvariantCulture
    );
    private bool _markerFallback = true;
    private MomentRewardPolicy _rewardPolicy;
    private string _rewardAmount = "0";
    private string _streamIdentity = string.Empty;
    private string _streamStatus = "Checking live stream…";
    private string _feedback = string.Empty;
    private bool _failed;
    private bool _featureEnabled;

    private string _weeklyUrl => $"/moments/{Uri.EscapeDataString(HostLogin)}";

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Moments,
                CancellationToken.None
            );
        if (!_featureEnabled)
        {
            return;
        }
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (HostId == 0)
        {
            return;
        }
        _page = await _moments.GetModeratorPageAsync(HostId, CancellationToken.None);
        if (_page is null)
        {
            return;
        }
        _mergeWindow = _page.Settings.MergeWindowSeconds.ToString(CultureInfo.InvariantCulture);
        _markerFallback = _page.Settings.MarkerFallbackEnabled;
        _rewardPolicy = _page.Settings.RewardPolicy;
        _rewardAmount = _page.Settings.RewardAmount;
        _drafts.Clear();
        foreach (var candidate in _page.Candidates)
        {
            _drafts[candidate.Public.PublicId] = new(
                candidate.Public.PublicTitle,
                candidate.Public.PublicCategory,
                candidate.PrivateRejectionReason,
                string.Empty
            );
        }
        var streamResult = await _streams
            .GetStreamLiveness(HostLogin)
            .ExecuteAsync(CancellationToken.None);
        var stream = streamResult.Match(
            static value => value,
            static _ => throw new UnreachableException()
        );
        switch (stream)
        {
            case HostStreamLivenessOutcome.Live live:
                _streamIdentity = live.StreamId;
                _streamStatus = $"Live stream: {live.StreamId}";
                break;
            case HostStreamLivenessOutcome.Offline:
                _streamIdentity = string.Empty;
                _streamStatus = "The selected channel is offline.";
                break;
            default:
                _streamIdentity = string.Empty;
                _streamStatus =
                    "We can’t confirm the live stream right now. Try again in a moment.";
                break;
        }
    }

    private MomentDraft Draft(Guid id) => _drafts[id];

    internal static string RewardPolicyLabel(MomentRewardPolicy policy) =>
        policy switch
        {
            MomentRewardPolicy.None => "No reward",
            MomentRewardPolicy.FirstRequester => "First viewer to request",
            MomentRewardPolicy.AllContributors => "All contributing viewers",
            _ => throw new UnreachableException("Unknown moment reward policy."),
        };

    internal static string CandidateStateLabel(MomentCandidateState state) =>
        state switch
        {
            MomentCandidateState.ProviderPending => "Creating clip",
            MomentCandidateState.ClipReady => "Clip ready",
            MomentCandidateState.MarkerReady => "Marker ready",
            MomentCandidateState.Failed => "Could not create clip",
            MomentCandidateState.Approved => "Approved",
            MomentCandidateState.Rejected => "Rejected",
            MomentCandidateState.Merged => "Merged into another moment",
            _ => throw new UnreachableException("Unknown moment candidate state."),
        };

    private Task SaveSettingsAsync() =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                if (!int.TryParse(_mergeWindow, out var mergeWindow))
                {
                    SetFeedback("The merge window must be a whole number.", true);
                    return;
                }
                var result = await _moments.ConfigureAsync(
                    HostId,
                    new ConfigureMomentHubCommand(
                        mergeWindow,
                        _markerFallback,
                        _rewardPolicy,
                        _rewardAmount
                    ),
                    CancellationToken.None
                );
                SetFeedback(
                    result.Match(
                        _ => "Moment settings saved.",
                        rejected => rejected.Reason.Message
                    ),
                    result is MomentResult<MomentHubSettingsView>.Rejected
                );
                await ReloadAsync();
            }
        );

    private Task CaptureAsync() =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                if (string.IsNullOrWhiteSpace(_streamIdentity))
                {
                    SetFeedback(_streamStatus, true);
                    return;
                }
                var session = PageContext.Session;
                var result = await _moments.CaptureAsync(
                    HostId,
                    new CaptureMomentCommand(
                        _streamIdentity,
                        new MomentViewerIdentity(session.Login, session.UserId, session.DisplayName)
                    ),
                    CancellationToken.None
                );
                SetFeedback(
                    result.Match(_ => "Moment captured.", rejected => rejected.Reason.Message),
                    result is MomentResult<MomentView>.Rejected
                );
                await ReloadAsync();
            }
        );

    private Task ApproveAsync(Guid id) =>
        MutateAsync(id, _moments.ApproveAsync, "Moment approved.");

    private Task EditAsync(Guid id) => MutateAsync(id, _moments.EditAsync, "Moment details saved.");

    private Task RejectAsync(Guid id) => MutateAsync(id, _moments.RejectAsync, "Moment rejected.");

    private Task MergeAsync(Guid id) =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var draft = Draft(id);
                if (!Guid.TryParse(draft.MergeTarget, out var target))
                {
                    SetFeedback("Enter the target moment number.", true);
                    return;
                }
                var result = await _moments.MergeAsync(
                    HostId,
                    id,
                    target,
                    ActorLogin,
                    draft.PrivateText,
                    CancellationToken.None
                );
                SetFeedback(
                    result.Match(_ => "Moments merged.", rejected => rejected.Reason.Message),
                    result is MomentResult<ModeratorMomentView>.Rejected
                );
                await ReloadAsync();
            }
        );

    private Task FinalizeWeekAsync() =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _moments.FinalizeWeekAsync(
                    HostId,
                    DateTime.UtcNow.AddDays(-7),
                    CancellationToken.None
                );
                SetFeedback(
                    result.Match(
                        succeeded =>
                            succeeded.WasIdempotent
                                ? "This week was already finalized."
                                : "Weekly winner finalized.",
                        rejected => rejected.Reason.Message
                    ),
                    result is MomentResult<MomentView>.Rejected
                );
                await ReloadAsync();
            }
        );

    private Task MutateAsync(
        Guid id,
        Func<
            int,
            ModerateMomentCommand,
            CancellationToken,
            Task<MomentResult<ModeratorMomentView>>
        > operation,
        string success
    ) =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var draft = Draft(id);
                var result = await operation(
                    HostId,
                    new ModerateMomentCommand(
                        id,
                        draft.Title,
                        draft.Category,
                        ActorLogin,
                        draft.PrivateText
                    ),
                    CancellationToken.None
                );
                SetFeedback(
                    result.Match(_ => success, rejected => rejected.Reason.Message),
                    result is MomentResult<ModeratorMomentView>.Rejected
                );
                await ReloadAsync();
            }
        );

    private void SetFeedback(string feedback, bool failed)
    {
        _feedback = feedback;
        _failed = failed;
    }

    private sealed class MomentDraft(
        string title,
        string category,
        string privateText,
        string mergeTarget
    )
    {
        public string Title { get; set; } = title;
        public string Category { get; set; } = category;
        public string PrivateText { get; set; } = privateText;
        public string MergeTarget { get; set; } = mergeTarget;
    }
}
