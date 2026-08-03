using System.Diagnostics;
using System.Globalization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.RequestBoards;

public partial class RequestBoardsPage
{
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

    private string _publicBoardUrl =>
        $"/requests/{Uri.EscapeDataString(HostLogin)}/{Uri.EscapeDataString(_draft.Slug)}";

    protected override async Task OnInitializedAsync()
    {
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
        _feedback = "New board ready. Complete its details, then Save board to create it.";

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

    internal static string RefundPolicyLabel(RequestBoardRefundPolicy policy) =>
        policy switch
        {
            RequestBoardRefundPolicy.Never => "Never refund",
            RequestBoardRefundPolicy.RejectedOrWithdrawn => "Refund if rejected or withdrawn",
            RequestBoardRefundPolicy.AnyUnfulfilledClosure => "Refund if not fulfilled",
            _ => throw new UnreachableException("Unknown request board refund policy."),
        };

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
