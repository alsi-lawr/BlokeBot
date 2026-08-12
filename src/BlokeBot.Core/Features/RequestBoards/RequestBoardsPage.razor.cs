using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.RequestBoards;

public partial class RequestBoardsPage
{
    private const string _paneTabsId = "request-board-pane";
    private const string _setupPaneKey = "setup";
    private const string _reviewPaneKey = "review";

    [Inject]
    private NavigationManager _navigation { get; set; } = null!;

    private IReadOnlyList<RequestBoardSummary> _boardList = [];
    private RequestBoardModeratorPage? _moderatorPage;
    private readonly Dictionary<long, ModerationDraft> _moderationDrafts = [];
    private BoardDraft _draft = BoardDraft.New();
    private Guid? _selectedFieldIdentity;
    private Guid? _fieldFocusIdentity;
    private string _feedback = string.Empty;
    private long _fieldFocusRequest;
    private long _primaryFocusRequest;
    private bool _isCreating = true;
    private bool _operationFailed;
    private bool _featureEnabled;
    private string _paneKey = _setupPaneKey;
    private readonly StudioOpenSet<RequestBoardStage> _openStages = new(RequestBoardStage.Basics);
    private readonly StudioOpenSet<long> _openModerationFolds = new();

    private const string _formPreviewBox =
        "overflow-hidden rounded-lg border border-[var(--app-control-border)] bg-[var(--app-control-bg)] px-[0.55rem] py-[0.32rem] text-[0.78rem] whitespace-nowrap text-ellipsis text-[var(--app-placeholder)]";

    private enum RequestBoardStage
    {
        Basics,
        Questions,
        CostAndRefunds,
        LimitsAndVoting,
    }

    private sealed record RefundChoice(
        RequestBoardRefundPolicy Policy,
        string Title,
        string Description
    );

    private static readonly IReadOnlyList<RefundChoice> _refundChoices =
    [
        new(
            RequestBoardRefundPolicy.Never,
            "Never",
            "Points are kept whatever happens to the request."
        ),
        new(
            RequestBoardRefundPolicy.RejectedOrWithdrawn,
            "Rejected or withdrawn",
            "Refund when you reject it or the viewer pulls it back."
        ),
        new(
            RequestBoardRefundPolicy.AnyUnfulfilledClosure,
            "Not fulfilled",
            "Refund any request that never gets completed."
        ),
    ];

    private static readonly IReadOnlyList<
        StudioSegmentedOption<RequestBoardFieldKind>
    > _fieldKindOptions =
    [
        new(RequestBoardFieldKind.Text, FieldKindLabel(RequestBoardFieldKind.Text)),
        new(RequestBoardFieldKind.Url, FieldKindLabel(RequestBoardFieldKind.Url)),
        new(RequestBoardFieldKind.Number, FieldKindLabel(RequestBoardFieldKind.Number)),
        new(RequestBoardFieldKind.Choice, FieldKindLabel(RequestBoardFieldKind.Choice)),
        new(RequestBoardFieldKind.TwitchClip, FieldKindLabel(RequestBoardFieldKind.TwitchClip)),
    ];

    private static readonly IReadOnlyList<StudioSegmentedOption<bool>> _requirementOptions =
    [
        new(true, "Required"),
        new(false, "Optional"),
    ];

    private string _publicBoardUrl =>
        $"/requests/{Uri.EscapeDataString(HostLogin)}/{Uri.EscapeDataString(_draft.Slug)}";

    private IReadOnlyList<StudioRailGroup> _railGroups =>
        [
            new(
                "Boards",
                [
                    .. _boardList.Select(board => new StudioRailItem
                    {
                        Key = board.Slug,
                        Label = board.Slug,
                        Search = $"{board.Slug} {board.Title}",
                        Sub = string.IsNullOrWhiteSpace(board.Title) ? null : board.Title,
                        Meta = board.IsOpen ? null : "closed",
                        Monospace = true,
                        On = board.IsOpen,
                        Selected = !_isCreating && board.Slug == _draft.Slug,
                        Select = EventCallback.Factory.Create(
                            this,
                            () => SelectBoardAsync(board.Slug)
                        ),
                    }),
                ],
                "No saved boards yet."
            ),
        ];

    private IReadOnlyList<SegmentedTabItem> _paneTabs =>
        [
            new(_setupPaneKey, "Set up"),
            new(_reviewPaneKey, _pendingCount > 0 ? $"Review · {_pendingCount}" : "Review"),
        ];

    private int _pendingCount =>
        _moderatorPage?.Submissions.Count(submission =>
            submission.Public.Status == RequestSubmissionStatus.Pending
        ) ?? 0;

    private string _headerTitle =>
        _isCreating ? "New board (not saved)"
        : string.IsNullOrWhiteSpace(_draft.Title) ? _draft.Slug
        : _draft.Title;

    private string? _headerStats =>
        _moderatorPage is { } moderation
            ? $"/{_draft.Slug} · {moderation.Submissions.Count} requests · {_pendingCount} awaiting review"
            : null;

    private string _slugOrExample => string.IsNullOrWhiteSpace(_draft.Slug) ? "games" : _draft.Slug;

    private string _basicsSummary => $"/{_slugOrExample} · viewers type !request {_slugOrExample}";

    private string _questionsSummary =>
        $"{Count(_draft.Fields.Count, "question")} · {_draft.Fields.Count(question => question.IsRequired)} required";

    private string _costSummary =>
        $"{(_draft.PointCost.Trim() == "0" ? "free" : $"{_draft.PointCost} points")} · {RefundPolicyProse(_draft.RefundPolicy)}";

    private string _limitsSummary =>
        $"{_draft.SubmissionLimit} active each · {CooldownProse()} · {(_draft.VotingEnabled ? $"voting on, {_draft.VoteLimit} votes each" : "voting off")}";

    private string CooldownProse() =>
        !int.TryParse(
            _draft.CooldownSeconds,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var seconds
        )
            ? $"{_draft.CooldownSeconds} s cooldown"
        : seconds == 0 ? "no cooldown"
        : $"{DurationProse.Format(seconds)} between requests";

    private static string Count(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string RefundPolicyProse(RequestBoardRefundPolicy policy) =>
        policy switch
        {
            RequestBoardRefundPolicy.Never => "no refunds",
            RequestBoardRefundPolicy.RejectedOrWithdrawn => "refunded if rejected or withdrawn",
            RequestBoardRefundPolicy.AnyUnfulfilledClosure => "refunded if not fulfilled",
            _ => throw new UnreachableException("Unknown request board refund policy."),
        };

    private IReadOnlyList<StudioChatLine> ChatPreviewLines() =>
        [
            new()
            {
                Speaker = "pixel_penny",
                SpeakerColour = "#e91e63",
                Message = $"!request {_slugOrExample} Outer Wilds | tags=chill,space",
                Monospace = true,
            },
            new()
            {
                Speaker = "BlokeBot",
                SpeakerColour = "#00ad6f",
                Badge = "BOT",
                Bot = true,
                Message = "Request #41 submitted for moderator review.",
            },
            new()
            {
                Speaker = "grumblesworth",
                SpeakerColour = "#1e90ff",
                Message = "!requestvote 41",
                Monospace = true,
            },
            new()
            {
                Speaker = "BlokeBot",
                SpeakerColour = "#00ad6f",
                Badge = "BOT",
                Bot = true,
                Message = "Vote recorded for request #41.",
            },
        ];

    private static string StatusPillClass(RequestSubmissionStatus status) =>
        status is RequestSubmissionStatus.Queued or RequestSubmissionStatus.Accepted
            ? "status-pill bg-[var(--app-affirmative-surface)] text-[var(--app-affirmative)]"
            : "status-pill bg-[var(--app-surface-muted)] text-[var(--app-text-muted)] ring-1 ring-[var(--app-border)]";

    private static string SubmittedStamp(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string KeyOrExample(BoardFieldDraft field) =>
        string.IsNullOrWhiteSpace(field.Key) ? "genre" : field.Key;

    private static string FieldPreviewPlaceholder(BoardFieldDraft field) =>
        field.Kind switch
        {
            RequestBoardFieldKind.Choice => "Choose an option ▾",
            RequestBoardFieldKind.Url => "https://…",
            RequestBoardFieldKind.TwitchClip => "https://clips.twitch.tv/…",
            RequestBoardFieldKind.Number
                when !string.IsNullOrWhiteSpace(field.MinimumNumber)
                    && !string.IsNullOrWhiteSpace(field.MaximumNumber) =>
                $"{field.MinimumNumber} to {field.MaximumNumber}",
            RequestBoardFieldKind.Number => "A number",
            _ => $"Up to {field.MaximumLength} characters",
        };

    private static IReadOnlyList<string> FieldChoices(BoardFieldDraft field) =>
        field.Choices.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

    private static IReadOnlyList<string> DraftTags(ModerationDraft draft) =>
        RequestBoardInput.ParseTags(draft.Tags);

    private static string AppendedList(IReadOnlyList<string> current, string value) =>
        current.Count == 0 ? value : $"{string.Join(", ", current)}, {value}";

    private static void AddChoice(BoardFieldDraft field)
    {
        var value = field.ChoiceDraft.Trim();
        if (value.Length == 0 || FieldChoices(field).Count >= RequestBoardLimits.MaximumChoices)
        {
            return;
        }

        field.Choices = AppendedList(FieldChoices(field), value);
        field.ChoiceDraft = string.Empty;
    }

    private static void AddChoiceOnEnter(BoardFieldDraft field, KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            AddChoice(field);
        }
    }

    private static void RemoveChoice(BoardFieldDraft field, string choice) =>
        field.Choices = string.Join(", ", FieldChoices(field).Where(value => value != choice));

    private static void AddTag(ModerationDraft draft)
    {
        var value = draft.TagDraft.Trim();
        if (value.Length == 0 || DraftTags(draft).Count >= RequestBoardLimits.MaximumTags)
        {
            return;
        }

        draft.Tags = AppendedList(DraftTags(draft), value);
        draft.TagDraft = string.Empty;
    }

    private static void AddTagOnEnter(ModerationDraft draft, KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            AddTag(draft);
        }
    }

    private static void RemoveTag(ModerationDraft draft, string tag) =>
        draft.Tags = string.Join(", ", DraftTags(draft).Where(value => value != tag));

    protected override async Task OnInitializedAsync()
    {
        _paneKey = SegmentedTabs.CanonicalKey(_navigation, _paneTabs);
        _ = await LoadPageContextAsync();
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.RequestBoards,
                CancellationToken.None
            );
        if (!_featureEnabled)
        {
            return;
        }
        await LoadBoardsAsync();
    }

    private async Task LoadBoardsAsync()
    {
        if (HostId == 0)
        {
            return;
        }

        _boardList = await _boards.GetBoardsForHostAsync(HostId, CancellationToken.None);
        if (_boardList.Count > 0 && string.IsNullOrWhiteSpace(_draft.Slug))
        {
            await SelectBoardAsync(_boardList[0].Slug);
            return;
        }

        if (_boardList.Count == 0 && _isCreating)
        {
            EnsureFieldSelection();
            SetCreateGuidance();
            _primaryFocusRequest++;
        }
    }

    private async Task SelectBoardAsync(string slug)
    {
        var board = _boardList.Single(value => value.Slug == slug);
        _draft = BoardDraft.From(board);
        _isCreating = false;
        _operationFailed = false;
        _feedback = string.Empty;
        _openModerationFolds.Reset();
        SelectFirstField();
        await LoadModeratorPageAsync();
    }

    private async Task LoadModeratorPageAsync()
    {
        if (HostId == 0 || string.IsNullOrWhiteSpace(_draft.Slug))
        {
            _moderatorPage = null;
            return;
        }

        _moderatorPage = await _boards.GetModeratorPageAsync(
            HostId,
            _draft.Slug,
            CancellationToken.None
        );
        _moderationDrafts.Clear();
        if (_moderatorPage is not null)
        {
            foreach (var submission in _moderatorPage.Submissions)
            {
                _moderationDrafts[submission.Public.Id] = ModerationDraft.From(submission);
            }
        }
    }

    private void NewBoard()
    {
        if (_isCreating)
        {
            return;
        }

        _draft = BoardDraft.New();
        _isCreating = true;
        _moderatorPage = null;
        _moderationDrafts.Clear();
        _operationFailed = false;
        _openModerationFolds.Reset();
        SelectFirstField();
        SetCreateGuidance();
        _primaryFocusRequest++;
    }

    private void AddField()
    {
        if (_draft.Fields.Count < RequestBoardLimits.MaximumFields)
        {
            var field = BoardFieldDraft.New();
            _draft.Fields.Add(field);
            SelectField(field);
        }
    }

    private void RemoveField(BoardFieldDraft field)
    {
        if (_draft.Fields.Count <= 1)
        {
            return;
        }

        var removedIndex = _draft.Fields.IndexOf(field);
        if (removedIndex < 0)
        {
            return;
        }

        _draft.Fields.RemoveAt(removedIndex);
        var neighbour = _draft.Fields[Math.Min(removedIndex, _draft.Fields.Count - 1)];
        SelectField(neighbour);
    }

    private Task SaveBoardAsync() =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var wasCreating = _isCreating;
                var command = _draft.ToCommand();
                if (command is null)
                {
                    _operationFailed = true;
                    _feedback = "Limits and numeric ranges must contain valid numbers.";
                    return;
                }

                var result = await _boards.ConfigureAsync(HostId, command, CancellationToken.None);
                switch (result)
                {
                    case RequestBoardResult<RequestBoardSummary>.Succeeded succeeded:
                        _draft = BoardDraft.From(succeeded.Value);
                        _isCreating = false;
                        _operationFailed = false;
                        _feedback = wasCreating ? "Board created." : "Board saved.";
                        SelectFirstField();
                        await LoadBoardsAsync();
                        await LoadModeratorPageAsync();
                        break;
                    case RequestBoardResult<RequestBoardSummary>.Rejected rejected:
                        _operationFailed = true;
                        _feedback = rejected.Reason.Message;
                        break;
                }
            }
        );

    private void SetCreateGuidance() =>
        _feedback = "New board ready. Complete its details, then select Create board.";

    private void SelectFirstField()
    {
        _selectedFieldIdentity = _draft.Fields.FirstOrDefault()?.Identity;
        _fieldFocusIdentity = null;
    }

    private void EnsureFieldSelection()
    {
        if (
            _selectedFieldIdentity is null
            || _draft.Fields.All(field => field.Identity != _selectedFieldIdentity)
        )
        {
            SelectFirstField();
        }
    }

    private void SelectField(BoardFieldDraft field)
    {
        _selectedFieldIdentity = field.Identity;
        _fieldFocusIdentity = field.Identity;
        _fieldFocusRequest++;
    }

    private bool IsFieldSelected(BoardFieldDraft field) => field.Identity == _selectedFieldIdentity;

    private long FieldFocusRequest(BoardFieldDraft field) =>
        field.Identity == _fieldFocusIdentity ? _fieldFocusRequest : 0;

    private static string FieldInventoryLabelId(BoardFieldDraft field) =>
        $"request-field-{field.Identity:N}-inventory-label";

    private static string FieldEditorRegionId(BoardFieldDraft field) =>
        $"request-field-{field.Identity:N}-editor";

    private static string FieldDisplayName(BoardFieldDraft field) =>
        string.IsNullOrWhiteSpace(field.Label) ? "Untitled field" : field.Label;

    private static string BoardFieldSummary(BoardFieldDraft field)
    {
        var key = string.IsNullOrWhiteSpace(field.Key) ? "No key" : field.Key;
        var requirement = field.IsRequired ? "Required" : "Optional";
        var detail = field.Kind switch
        {
            RequestBoardFieldKind.Choice when !string.IsNullOrWhiteSpace(field.Choices) =>
                $"{field.Choices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length} choices",
            RequestBoardFieldKind.Number
                when !string.IsNullOrWhiteSpace(field.MinimumNumber)
                    || !string.IsNullOrWhiteSpace(field.MaximumNumber) =>
                $"Range {OptionalBoundary(field.MinimumNumber)} to {OptionalBoundary(field.MaximumNumber)}",
            _ => $"Maximum {field.MaximumLength} characters",
        };
        return $"{key} · {FieldKindLabel(field.Kind)} · {requirement} · {detail}";
    }

    private static string OptionalBoundary(string value) =>
        string.IsNullOrWhiteSpace(value) ? "any" : value;

    internal static string FieldKindLabel(RequestBoardFieldKind kind) =>
        kind switch
        {
            RequestBoardFieldKind.Text => "Text",
            RequestBoardFieldKind.Url => "Link",
            RequestBoardFieldKind.Choice => "Choose from a list",
            RequestBoardFieldKind.Number => "Number",
            RequestBoardFieldKind.TwitchClip => "Twitch clip link",
            _ => throw new UnreachableException("Unknown request board field type."),
        };

    internal static string SubmissionStatusLabel(RequestSubmissionStatus status) =>
        status switch
        {
            RequestSubmissionStatus.Pending => "Awaiting review",
            RequestSubmissionStatus.Approved => "Approved",
            RequestSubmissionStatus.Queued => "In queue",
            RequestSubmissionStatus.Accepted => "Accepted",
            RequestSubmissionStatus.Completed => "Completed",
            RequestSubmissionStatus.Rejected => "Rejected",
            RequestSubmissionStatus.Withdrawn => "Withdrawn",
            RequestSubmissionStatus.Merged => "Merged into another request",
            _ => throw new UnreachableException("Unknown request submission status."),
        };

    internal static string ModerationActionLabel(RequestSubmissionStatus target) =>
        target switch
        {
            RequestSubmissionStatus.Approved => "Approve",
            RequestSubmissionStatus.Queued => "Add to queue",
            RequestSubmissionStatus.Accepted => "Mark accepted",
            RequestSubmissionStatus.Completed => "Mark complete",
            RequestSubmissionStatus.Rejected => "Reject",
            _ => throw new UnreachableException("Unknown request moderation target."),
        };

    internal static string ReservationStateLabel(RequestPointReservationState state) =>
        state switch
        {
            RequestPointReservationState.None => "No points charged",
            RequestPointReservationState.Reserved => "Points held",
            RequestPointReservationState.Refunded => "Points refunded",
            RequestPointReservationState.Consumed => "Points charged",
            _ => throw new UnreachableException("Unknown request point reservation state."),
        };

    private Task TransitionAsync(long submissionId, RequestSubmissionStatus target) =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var draft = _moderationDrafts[submissionId];
                var command = draft.ToCommand(submissionId, target);
                if (command is null)
                {
                    _operationFailed = true;
                    _feedback = "Priority must be a whole number from -1000 to 1000.";
                    return;
                }

                var result = await _boards.ModerateAsync(HostId, command, CancellationToken.None);
                _feedback = result.Match(
                    _ =>
                        $"Request #{submissionId} is now {SubmissionStatusLabel(target).ToLowerInvariant()}.",
                    rejected => rejected.Reason.Message
                );
                _operationFailed =
                    result is RequestBoardResult<ModeratorRequestSubmissionView>.Rejected;
                await LoadModeratorPageAsync();
            }
        );

    private Task MergeAsync(long submissionId) =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var draft = _moderationDrafts[submissionId];
                if (
                    !long.TryParse(
                        draft.MergeTarget,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var target
                    )
                )
                {
                    _operationFailed = true;
                    _feedback = "Enter the target request number before merging.";
                    return;
                }

                var result = await _boards.MergeAsync(
                    HostId,
                    submissionId,
                    target,
                    draft.PublicNote,
                    draft.PrivateNote,
                    CancellationToken.None
                );
                _feedback = result.Match(
                    _ => $"Request #{submissionId} was merged into #{target}.",
                    rejected => rejected.Reason.Message
                );
                _operationFailed =
                    result is RequestBoardResult<ModeratorRequestSubmissionView>.Rejected;
                await LoadModeratorPageAsync();
            }
        );

    private static IReadOnlyList<RequestSubmissionStatus> AvailableTransitions(
        RequestSubmissionStatus status
    ) =>
        status switch
        {
            RequestSubmissionStatus.Pending =>
            [
                RequestSubmissionStatus.Approved,
                RequestSubmissionStatus.Rejected,
            ],
            RequestSubmissionStatus.Approved =>
            [
                RequestSubmissionStatus.Queued,
                RequestSubmissionStatus.Accepted,
                RequestSubmissionStatus.Rejected,
            ],
            RequestSubmissionStatus.Queued =>
            [
                RequestSubmissionStatus.Accepted,
                RequestSubmissionStatus.Completed,
                RequestSubmissionStatus.Rejected,
            ],
            RequestSubmissionStatus.Accepted =>
            [
                RequestSubmissionStatus.Completed,
                RequestSubmissionStatus.Rejected,
            ],
            _ => [],
        };

    private sealed class BoardDraft
    {
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsOpen { get; set; } = true;
        public string PointCost { get; set; } = "0";
        public RequestBoardRefundPolicy RefundPolicy { get; set; } =
            RequestBoardRefundPolicy.RejectedOrWithdrawn;
        public string SubmissionLimit { get; set; } = "3";
        public string CooldownSeconds { get; set; } = "0";
        public string VoteLimit { get; set; } = "10";
        public bool VotingEnabled { get; set; } = true;
        public List<BoardFieldDraft> Fields { get; } = [];

        public ConfigureRequestBoardCommand? ToCommand()
        {
            if (
                !int.TryParse(
                    SubmissionLimit,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var submissionLimit
                )
                || !int.TryParse(
                    CooldownSeconds,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var cooldown
                )
                || !int.TryParse(
                    VoteLimit,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var voteLimit
                )
            )
            {
                return null;
            }

            var fields = new List<RequestBoardFieldCommand>(Fields.Count);
            foreach (var field in Fields)
            {
                var command = field.ToCommand();
                if (command is null)
                {
                    return null;
                }

                fields.Add(command);
            }

            return new ConfigureRequestBoardCommand(
                Slug,
                Title,
                Description,
                IsOpen,
                PointCost,
                RefundPolicy,
                submissionLimit,
                cooldown,
                voteLimit,
                VotingEnabled,
                fields
            );
        }

        public static BoardDraft New()
        {
            var value = new BoardDraft();
            value.Fields.Add(BoardFieldDraft.New());
            return value;
        }

        public static BoardDraft From(RequestBoardSummary board)
        {
            var value = new BoardDraft
            {
                Slug = board.Slug,
                Title = board.Title,
                Description = board.Description,
                IsOpen = board.IsOpen,
                PointCost = board.PointCost,
                RefundPolicy = board.RefundPolicy,
                SubmissionLimit = board.SubmissionLimitPerUser.ToString(
                    CultureInfo.InvariantCulture
                ),
                CooldownSeconds = board.SubmissionCooldownSeconds.ToString(
                    CultureInfo.InvariantCulture
                ),
                VoteLimit = board.VoteLimitPerUser.ToString(CultureInfo.InvariantCulture),
                VotingEnabled = board.VotingEnabled,
            };
            value.Fields.AddRange(board.Fields.Select(BoardFieldDraft.From));
            return value;
        }
    }

    private sealed class BoardFieldDraft
    {
        public Guid Identity { get; } = Guid.NewGuid();
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public RequestBoardFieldKind Kind { get; set; }
        public bool IsRequired { get; set; } = true;
        public string MaximumLength { get; set; } = "500";
        public string MinimumNumber { get; set; } = string.Empty;
        public string MaximumNumber { get; set; } = string.Empty;
        public string Choices { get; set; } = string.Empty;
        public string ChoiceDraft { get; set; } = string.Empty;

        public RequestBoardFieldCommand? ToCommand() =>
            !int.TryParse(
                MaximumLength,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var maximumLength
            )
            || !TryOptionalDecimal(MinimumNumber, out var minimum)
            || !TryOptionalDecimal(MaximumNumber, out var maximum)
                ? null
                : new RequestBoardFieldCommand(
                    Key,
                    Label,
                    Kind,
                    IsRequired,
                    maximumLength,
                    minimum,
                    maximum,
                    Choices.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                );

        public static BoardFieldDraft New() => new() { Key = "details", Label = "Details" };

        public static BoardFieldDraft From(RequestBoardFieldView field) =>
            new()
            {
                Key = field.Key,
                Label = field.Label,
                Kind = field.Kind,
                IsRequired = field.IsRequired,
                MaximumLength = field.MaximumLength.ToString(CultureInfo.InvariantCulture),
                MinimumNumber =
                    field.MinimumNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                MaximumNumber =
                    field.MaximumNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Choices = string.Join(", ", field.Choices),
            };

        private static bool TryOptionalDecimal(string value, out decimal? parsed)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                parsed = null;
                return true;
            }

            var valid = decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number
            );
            parsed = valid ? number : null;
            return valid;
        }
    }

    private sealed class ModerationDraft
    {
        public string Category { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public string Priority { get; set; } = "0";
        public string PublicNote { get; set; } = string.Empty;
        public string PrivateNote { get; set; } = string.Empty;
        public string PrivateRejectionReason { get; set; } = string.Empty;
        public string MergeTarget { get; set; } = string.Empty;
        public string TagDraft { get; set; } = string.Empty;

        public ModerateRequestCommand? ToCommand(
            long submissionId,
            RequestSubmissionStatus target
        ) =>
            int.TryParse(
                Priority,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var priority
            )
                ? new ModerateRequestCommand(
                    submissionId,
                    target,
                    PublicNote,
                    PrivateNote,
                    PrivateRejectionReason,
                    priority,
                    Category,
                    RequestBoardInput.ParseTags(Tags)
                )
                : null;

        public static ModerationDraft From(ModeratorRequestSubmissionView submission) =>
            new()
            {
                Category = submission.Public.Category,
                Tags = string.Join(", ", submission.Public.Tags),
                Priority = submission.Public.Priority.ToString(CultureInfo.InvariantCulture),
                PublicNote = submission.Public.PublicNote,
                PrivateNote = submission.PrivateModeratorNote,
                PrivateRejectionReason = submission.PrivateRejectionReason,
            };
    }
}
