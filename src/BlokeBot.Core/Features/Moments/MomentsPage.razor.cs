using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Moments;

public partial class MomentsPage
{
    /// <summary>
    /// A stand-in moment number for the static preview. The reply text itself comes from the
    /// command module, so the preview cannot drift from what chat is told.
    /// </summary>
    private static readonly Guid _previewMomentId = new("8f3a2159-4c1d-4f8e-9a2b-6d0e7c5148af");

    private static readonly IReadOnlyList<StudioChatLine> _capturePreview =
    [
        ViewerLine("pixel_penny", "#e91e63", "that boss skip was unreal", monospace: false),
        ViewerLine("grumblesworth", "#1e90ff", "!moment", monospace: true),
        BotLine(MomentCommandModule.CapturedReply(_previewMomentId)),
        ViewerLine("saltlick", "#ff7f50", "!clip", monospace: true),
        BotLine(MomentCommandModule.JoinedReply(_previewMomentId)),
    ];

    private static readonly IReadOnlyList<
        StudioSegmentedOption<MomentRewardPolicy>
    > _rewardPolicies =
    [
        .. Enum.GetValues<MomentRewardPolicy>()
            .Select(policy => new StudioSegmentedOption<MomentRewardPolicy>(
                policy,
                RewardPolicyLabel(policy)
            )),
    ];

    private static readonly IReadOnlyList<StudioSegmentedOption<MomentStateFilter>> _stateFilters =
    [
        new(MomentStateFilter.All, "All"),
        new(MomentStateFilter.NeedsReview, "Needs review"),
        new(MomentStateFilter.Approved, "Approved"),
        new(MomentStateFilter.Rejected, "Rejected"),
    ];

    private readonly Dictionary<Guid, MomentDraft> _drafts = [];
    private readonly HashSet<MomentStage> _openStages = [];
    private readonly HashSet<MomentFold> _openFolds = [];

    private MomentModeratorPage? _page;
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
    private MomentStateFilter _stateFilter = MomentStateFilter.All;
    private Guid? _selectedId;

    private string _weeklyUrl => $"/moments/{Uri.EscapeDataString(HostLogin)}";

    private enum MomentStage
    {
        Capture,
        Reward,
    }

    private enum MomentFold
    {
        PrivateNote,
        Merge,
    }

    private enum MomentStateFilter
    {
        All,
        NeedsReview,
        Approved,
        Rejected,
    }

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

    private bool IsStageOpen(MomentStage stage) => _openStages.Contains(stage);

    private void SetStage(MomentStage stage, bool open) =>
        _ = open ? _openStages.Add(stage) : _openStages.Remove(stage);

    private bool IsFoldOpen(MomentFold fold) => _openFolds.Contains(fold);

    private void SetFold(MomentFold fold, bool open) =>
        _ = open ? _openFolds.Add(fold) : _openFolds.Remove(fold);

    private void ToggleMarkerFallback() => _markerFallback = !_markerFallback;

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

    /// <summary>
    /// The art behind a candidate's glyph. No clip imagery is fetched by the backend, so each state
    /// gets a treatment instead: a lit gradient for a usable clip, hatching for a marker, and a
    /// faded one for anything that is pending, failed or already decided.
    /// </summary>
    internal static string ThumbnailClass(MomentCandidateState state) =>
        state switch
        {
            MomentCandidateState.ClipReady =>
                "aspect-video bg-linear-to-br from-slate-700 to-slate-900 text-3xl text-white",
            MomentCandidateState.Approved =>
                "aspect-video bg-linear-to-br from-slate-800 to-slate-600 text-3xl text-white",
            MomentCandidateState.MarkerReady =>
                "aspect-video bg-[repeating-linear-gradient(-45deg,var(--app-surface-muted)_0_12px,var(--app-background)_12px_24px)] text-3xl text-muted-foreground",
            _ =>
                "aspect-video bg-linear-to-br from-slate-600 to-slate-800 text-3xl text-white opacity-55",
        };

    internal static string StateGlyph(MomentCandidateState state) =>
        state switch
        {
            MomentCandidateState.ProviderPending => "◌",
            MomentCandidateState.MarkerReady => "⚑",
            MomentCandidateState.Failed => "⚠",
            MomentCandidateState.Rejected => "✕",
            _ => "▶",
        };

    /// <summary>
    /// Green marks a usable or approved moment and red marks the provider failure, which is the
    /// only red on the page; every other state stays neutral.
    /// </summary>
    internal static string StatePillClass(MomentCandidateState state) =>
        state switch
        {
            MomentCandidateState.ClipReady or MomentCandidateState.Approved =>
                "status-pill bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200",
            MomentCandidateState.Failed => "status-pill bg-red-50 text-red-700 ring-1 ring-red-200",
            _ => "status-pill bg-slate-100 text-slate-600 ring-1 ring-slate-200",
        };

    private static string CandidateTitle(ModeratorMomentView candidate) =>
        candidate.Public.PublicTitle.Length == 0 ? "Untitled moment" : candidate.Public.PublicTitle;

    private static string CandidateSubline(ModeratorMomentView candidate) =>
        string.Join(
            " · ",
            Count(candidate.Public.Contributors.Count, "contributor"),
            Count(candidate.Public.VoteCount, "vote"),
            $"{candidate.Public.CapturedAtUtc:HH:mm} UTC"
        );

    private static string ReviewMeta(ModeratorMomentView candidate) =>
        string.Join(
            " · ",
            Count(candidate.Public.Contributors.Count, "contributor"),
            Count(candidate.Public.VoteCount, "vote"),
            candidate.Public.PublicId.ToString(),
            candidate.Public.ApprovedAtUtc is { } approved
                ? $"approved {approved:yyyy-MM-dd HH:mm} UTC"
                : $"captured {candidate.Public.CapturedAtUtc:yyyy-MM-dd HH:mm} UTC"
        );

    private static string Count(int value, string noun) =>
        value == 1 ? $"1 {noun}" : $"{value.ToString(CultureInfo.CurrentCulture)} {noun}s";

    private IReadOnlyList<ModeratorMomentView> VisibleCandidates() =>
        _page is null ? []
        : _stateFilter is MomentStateFilter.All ? _page.Candidates
        : [.. _page.Candidates.Where(candidate => Matches(candidate.Public.State))];

    private bool Matches(MomentCandidateState state) =>
        _stateFilter switch
        {
            MomentStateFilter.NeedsReview => state
                is MomentCandidateState.ProviderPending
                    or MomentCandidateState.ClipReady
                    or MomentCandidateState.MarkerReady
                    or MomentCandidateState.Failed,
            MomentStateFilter.Approved => state is MomentCandidateState.Approved,
            MomentStateFilter.Rejected => state is MomentCandidateState.Rejected,
            _ => true,
        };

    /// <summary>
    /// Resolves the selection against what is on screen rather than storing it, so filtering out
    /// the chosen candidate moves the review panel to the first one still visible instead of
    /// leaving it reviewing something nobody can see.
    /// </summary>
    private ModeratorMomentView? SelectedCandidate()
    {
        var visible = VisibleCandidates();
        return visible.FirstOrDefault(candidate => candidate.Public.PublicId == _selectedId)
            ?? visible.FirstOrDefault();
    }

    private string CandidateCount()
    {
        if (_page is null)
        {
            return string.Empty;
        }

        var total = Count(_page.Candidates.Count, "moment");
        return _stateFilter is MomentStateFilter.All
            ? total
            : $"{VisibleCandidates().Count.ToString(CultureInfo.CurrentCulture)} of {total}";
    }

    private string FilterNoun() =>
        _stateFilter switch
        {
            MomentStateFilter.NeedsReview => "unreviewed",
            MomentStateFilter.Approved => "approved",
            MomentStateFilter.Rejected => "rejected",
            _ => string.Empty,
        };

    private IReadOnlyList<ModeratorMomentView> MergeTargets(ModeratorMomentView source) =>
        _page is null
            ? []
            :
            [
                .. _page.Candidates.Where(candidate =>
                    candidate.Public.PublicId != source.Public.PublicId
                ),
            ];

    private static string MergeTargetLabel(ModeratorMomentView target) =>
        $"{CandidateTitle(target)} · {target.Public.PublicId} ({CandidateStateLabel(target.Public.State)})";

    /// <summary>
    /// One adoptable part per viewer suggestion. The same wording suggested twice is one chip.
    /// </summary>
    private static IReadOnlyList<MomentSuggestionChip> SuggestionChips(
        ModeratorMomentView candidate
    ) =>
        [
            .. candidate
                .Suggestions.SelectMany(static suggestion =>
                    new[]
                    {
                        suggestion.Title is { Length: > 0 } title
                            ? new MomentSuggestionChip("title", $"“{title}”", title)
                            : null,
                        suggestion.Category is { Length: > 0 } category
                            ? new MomentSuggestionChip("category", $"category={category}", category)
                            : null,
                    }.OfType<MomentSuggestionChip>()
                )
                .Distinct(),
        ];

    private static void UseSuggestion(MomentDraft draft, MomentSuggestionChip chip)
    {
        if (chip.Field == "title")
        {
            draft.Title = chip.Value;
            return;
        }

        draft.Category = chip.Value;
    }

    private string CaptureSummary() =>
        $"{MergeWindowProse()} merge window · marker fallback {(_markerFallback ? "on" : "off")}";

    private string MergeWindowProse()
    {
        var entered = _mergeWindow.Trim();
        return int.TryParse(
                entered,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var seconds
            )
                ? DurationProse.Format(seconds)
            : entered.Length == 0 ? "no"
            : entered;
    }

    private string RewardSummary() =>
        _rewardPolicy is MomentRewardPolicy.None
            ? RewardPolicyLabel(_rewardPolicy)
            : $"{RewardPolicyLabel(_rewardPolicy)} · {_rewardAmount} points";

    private static StudioChatLine ViewerLine(
        string speaker,
        string colour,
        string message,
        bool monospace
    ) =>
        new()
        {
            Speaker = speaker,
            SpeakerColour = colour,
            Message = message,
            Monospace = monospace,
        };

    private static StudioChatLine BotLine(string message) =>
        new()
        {
            Speaker = "BlokeBot",
            SpeakerColour = "#00ad6f",
            Badge = "BOT",
            Bot = true,
            Message = message,
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
                    SetFeedback("Choose the moment to merge into.", true);
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

    private sealed record MomentSuggestionChip(string Field, string Text, string Value);

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
