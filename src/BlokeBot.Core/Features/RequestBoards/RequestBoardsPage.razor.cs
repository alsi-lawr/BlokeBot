using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.RequestBoards;

public partial class RequestBoardsPage
{
    private IReadOnlyList<RequestBoardSummary> _boardList = [];
    private RequestBoardModeratorPage? _moderatorPage;
    private readonly Dictionary<long, ModerationDraft> _moderationDrafts = [];
    private BoardDraft _draft = BoardDraft.New();
    private string _feedback = string.Empty;
    private bool _operationFailed;

    private string _publicBoardUrl =>
        string.IsNullOrWhiteSpace(_draft.Slug) || string.IsNullOrWhiteSpace(HostLogin)
            ? "#"
            : $"/requests/{Uri.EscapeDataString(HostLogin)}/{Uri.EscapeDataString(_draft.Slug)}";

    protected override async Task OnInitializedAsync()
    {
        await LoadPageContextAsync();
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
        }
    }

    private async Task SelectBoardAsync(string slug)
    {
        var board = _boardList.Single(value => value.Slug == slug);
        _draft = BoardDraft.From(board);
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
        _draft = BoardDraft.New();
        _moderatorPage = null;
        _moderationDrafts.Clear();
        _feedback = string.Empty;
    }

    private void AddField()
    {
        if (_draft.Fields.Count < RequestBoardLimits.MaximumFields)
        {
            _draft.Fields.Add(BoardFieldDraft.New());
        }
    }

    private void RemoveField(BoardFieldDraft field)
    {
        if (_draft.Fields.Count > 1)
        {
            _draft.Fields.Remove(field);
        }
    }

    private Task SaveBoardAsync()
    {
        return RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var command = _draft.ToCommand();
                if (command is null)
                {
                    _operationFailed = true;
                    _feedback = "Limits and numeric ranges must contain valid numbers.";
                    return;
                }

                var result = await _boards.ConfigureAsync(HostId, command, CancellationToken.None);
                _feedback = result.Match(
                    succeeded =>
                    {
                        _draft = BoardDraft.From(succeeded.Value);
                        return "Board saved.";
                    },
                    rejected => rejected.Reason.Message
                );
                _operationFailed = result is RequestBoardResult<RequestBoardSummary>.Rejected;
                await LoadBoardsAsync();
                await LoadModeratorPageAsync();
            }
        );
    }

    private Task TransitionAsync(long submissionId, RequestSubmissionStatus target)
    {
        return RunSelectedHostMutationAsync(
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
                    _ => $"Request #{submissionId} is now {target}.",
                    rejected => rejected.Reason.Message
                );
                _operationFailed =
                    result is RequestBoardResult<ModeratorRequestSubmissionView>.Rejected;
                await LoadModeratorPageAsync();
            }
        );
    }

    private Task MergeAsync(long submissionId)
    {
        return RunSelectedHostMutationAsync(
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
                    _feedback = "Enter the target request ID before merging.";
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
    }

    private static IReadOnlyList<RequestSubmissionStatus> AvailableTransitions(
        RequestSubmissionStatus status
    )
    {
        return status switch
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
    }

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

        public RequestBoardFieldCommand? ToCommand()
        {
            if (
                !int.TryParse(
                    MaximumLength,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var maximumLength
                )
                || !TryOptionalDecimal(MinimumNumber, out var minimum)
                || !TryOptionalDecimal(MaximumNumber, out var maximum)
            )
            {
                return null;
            }

            return new RequestBoardFieldCommand(
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
        }

        public static BoardFieldDraft New()
        {
            return new() { Key = "details", Label = "Details" };
        }

        public static BoardFieldDraft From(RequestBoardFieldView field)
        {
            return new()
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
        }

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

        public ModerateRequestCommand? ToCommand(long submissionId, RequestSubmissionStatus target)
        {
            return int.TryParse(
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
        }

        public static ModerationDraft From(ModeratorRequestSubmissionView submission)
        {
            return new()
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
}
