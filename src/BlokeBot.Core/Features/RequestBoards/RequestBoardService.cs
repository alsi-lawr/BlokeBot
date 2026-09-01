using System.Globalization;
using System.Numerics;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.RequestBoards;

public sealed class RequestBoardService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider
)
{
    private const int _eventSchemaVersion = 1;
    private const int _maximumEventPayloadLength = 1024;
    private const int _maximumEventReadCount = 200;
    private const int _retryGateCount = 64;
    private static readonly SemaphoreSlim[] _submissionRetryGates = CreateRetryGates();
    private static readonly SemaphoreSlim[] _voteRetryGates = CreateRetryGates();

    public async Task<RequestBoardResult<RequestBoardSummary>> ConfigureAsync(
        int hostId,
        ConfigureRequestBoardCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<RequestBoardSummary>(new RequestBoardRejection.FeatureDisabled());
        }

        var validation = ValidateConfiguration(hostId, command);
        if (validation is not null)
        {
            return Rejected<RequestBoardSummary>(validation);
        }

        var slug = CommunityInput.NormalizeSlug(command.Slug);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Hosts.AnyAsync(host => host.Id == hostId, ct))
        {
            return Rejected<RequestBoardSummary>(
                new RequestBoardRejection.NotFound("The selected host does not exist.")
            );
        }

        var board = await db
            .RequestBoards.Include(value => value.Fields)
            .SingleOrDefaultAsync(value => value.HostId == hostId && value.Slug == slug, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var replaceFields = false;
        if (board is null)
        {
            board = new RequestBoard
            {
                HostId = hostId,
                Slug = slug,
                CreatedAtUtc = now,
            };
            _ = db.RequestBoards.Add(board);
        }
        else if (!FieldShapeMatches(board.Fields, command.Fields))
        {
            if (
                await db.RequestSubmissions.AnyAsync(
                    submission => submission.BoardId == board.Id,
                    ct
                )
            )
            {
                return Rejected<RequestBoardSummary>(
                    new RequestBoardRejection.Conflict(
                        "Submission fields cannot be replaced after the board has submissions."
                    )
                );
            }

            replaceFields = true;
        }

        board.Title = command.Title.Trim();
        board.Description = command.Description.Trim();
        board.IsOpen = command.IsOpen;
        board.PointCost = BigInteger
            .Parse(command.PointCost, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
        board.RefundPolicy = command.RefundPolicy;
        board.SubmissionLimitPerUser = command.SubmissionLimitPerUser;
        board.SubmissionCooldownSeconds = command.SubmissionCooldownSeconds;
        board.VoteLimitPerUser = command.VoteLimitPerUser;
        board.VotingEnabled = command.VotingEnabled;
        board.UpdatedAtUtc = now;
        if (replaceFields)
        {
            db.RequestBoardFields.RemoveRange(board.Fields);
            board.Fields.Clear();
        }

        if (board.Fields.Count == 0)
        {
            board.Fields.AddRange(
                command.Fields.Select(
                    (field, position) =>
                        new RequestBoardField
                        {
                            Position = position,
                            Key = CommunityInput.NormalizeSlug(field.Key),
                            Label = field.Label.Trim(),
                            Kind = field.Kind,
                            IsRequired = field.IsRequired,
                            MaximumLength = EffectiveMaximumLength(field),
                            MinimumNumber = field.MinimumNumber,
                            MaximumNumber = field.MaximumNumber,
                            ChoiceOptions = string.Join('\n', field.Choices ?? []),
                        }
                )
            );
        }

        _ = await db.SaveChangesAsync(ct);
        AddEvent(
            db,
            board,
            null,
            RequestBoardEventKind.BoardConfigured,
            new
            {
                board.Slug,
                board.Title,
                board.IsOpen,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _ = await events.PublishAsync(AppEventKind.RequestBoardsChanged, ct);
        return Succeeded(await LoadSummaryAsync(db, board, ct));
    }

    public async Task<RequestBoardResult<PublicRequestSubmissionView>> SubmitAsync(
        int hostId,
        string boardSlug,
        SubmitRequestCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.FeatureDisabled()
            );
        }

        if (command.OperationId == Guid.Empty)
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.Invalid("A submission operation ID is required.")
            );
        }

        var login = CommunityInput.NormalizeLogin(command.SubmitterLogin);
        return !CommunityInput.IsValidLogin(login)
            ? Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.Invalid("A valid Twitch login is required.")
            )
            : await ExecuteWithCollisionRecoveryAsync(
                RetryGateFor(_submissionRetryGates, HashCode.Combine(hostId, command.OperationId)),
                () => SubmitAttemptAsync(hostId, boardSlug, command, login, ct),
                () =>
                    LoadCommittedSubmissionRetryAsync(
                        hostId,
                        boardSlug,
                        command.OperationId,
                        login,
                        ct
                    ),
                ct
            );
    }

    private async Task<RequestBoardResult<PublicRequestSubmissionView>> SubmitAttemptAsync(
        int hostId,
        string boardSlug,
        SubmitRequestCommand command,
        string login,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db
            .RequestSubmissions.Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.OperationId == command.OperationId,
                ct
            );
        if (existing is not null)
        {
            return
                (
                    existing.Board?.Slug != boardSlug
                    && !await db.RequestBoards.AnyAsync(
                        board =>
                            board.Id == existing.BoardId
                            && board.Slug == CommunityInput.NormalizeSlug(boardSlug),
                        ct
                    )
                ) || !string.Equals(existing.SubmitterLogin, login, StringComparison.Ordinal)
                ? Rejected<PublicRequestSubmissionView>(
                    new RequestBoardRejection.Conflict(
                        "That operation ID belongs to another submission."
                    )
                )
                : new RequestBoardResult<PublicRequestSubmissionView>.Succeeded(
                    ToPublicView(existing),
                    true
                );
        }

        var slug = CommunityInput.NormalizeSlug(boardSlug);
        var board = await db
            .RequestBoards.Include(value => value.Fields)
            .SingleOrDefaultAsync(value => value.HostId == hostId && value.Slug == slug, ct);
        if (board is null)
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.NotFound("Request board not found.")
            );
        }

        if (!board.IsOpen)
        {
            return Rejected<PublicRequestSubmissionView>(new RequestBoardRejection.Closed());
        }

        var input = ValidateSubmission(board, command);
        if (input.Rejection is not null)
        {
            return Rejected<PublicRequestSubmissionView>(input.Rejection);
        }

        var activeStatuses = ActiveSubmissionStatuses();
        var activeCount = await db.RequestSubmissions.CountAsync(
            value =>
                value.BoardId == board.Id
                && value.SubmitterLogin == login
                && activeStatuses.Contains(value.Status),
            ct
        );
        if (activeCount >= board.SubmissionLimitPerUser)
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.LimitReached(
                    $"This board allows {board.SubmissionLimitPerUser} active requests per viewer."
                )
            );
        }

        var lastSubmissionAt = await db
            .RequestSubmissions.Where(value =>
                value.BoardId == board.Id && value.SubmitterLogin == login
            )
            .MaxAsync(value => (DateTime?)value.CreatedAtUtc, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (lastSubmissionAt is { } last && last.AddSeconds(board.SubmissionCooldownSeconds) > now)
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.Cooldown(last.AddSeconds(board.SubmissionCooldownSeconds))
            );
        }

        var cost = PointAmount.ParseAbsolute(board.PointCost);
        var balance = await LoadBalanceAsync(db, hostId, login, now, ct);
        var currentBalance = PointAmount.ParseAbsolute(balance.Amount);
        if (currentBalance.CompareTo(cost) < 0)
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.InsufficientPoints(cost.ToDisplayString())
            );
        }

        var submission = new RequestSubmission
        {
            HostId = hostId,
            BoardId = board.Id,
            OperationId = command.OperationId,
            SubmitterLogin = login,
            Title = input.Title,
            NormalizedTitle = input.NormalizedTitle,
            NormalizedUrl = input.NormalizedUrl,
            Status = RequestSubmissionStatus.Pending,
            Category = input.Category,
            Tags = RequestBoardInput.JoinTags(input.Tags),
            PointReservationState = cost.IsZero
                ? RequestPointReservationState.None
                : RequestPointReservationState.Reserved,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        foreach (var field in board.Fields.OrderBy(value => value.Position))
        {
            if (input.Values.TryGetValue(field.Id, out var value))
            {
                submission.Values.Add(
                    new RequestSubmissionValue
                    {
                        FieldId = field.Id,
                        Field = field,
                        Value = value,
                    }
                );
            }
        }

        _ = db.RequestSubmissions.Add(submission);
        _ = await db.SaveChangesAsync(ct);
        if (!cost.IsZero)
        {
            var next = currentBalance.Subtract(cost);
            balance.Amount = next.ToString();
            balance.UpdatedAtUtc = now;
            AddPointLedger(
                db,
                submission,
                PointLedgerKind.RequestReservation,
                -cost.Value,
                next,
                "Request-board point reservation",
                now
            );
            AddEvent(
                db,
                board,
                submission.Id,
                RequestBoardEventKind.PointsReserved,
                new { submission.Id, Cost = cost.ToString() },
                now
            );
        }

        AddEvent(
            db,
            board,
            submission.Id,
            RequestBoardEventKind.Submitted,
            new
            {
                submission.Id,
                submission.Title,
                submission.Category,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _ = await events.PublishAsync(AppEventKind.RequestBoardsChanged, ct);
        return Succeeded(ToPublicView(submission));
    }

    public async Task<RequestBoardResult<PublicRequestSubmissionView>> VoteAsync(
        int hostId,
        long submissionId,
        string voterLogin,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.FeatureDisabled()
            );
        }

        var login = CommunityInput.NormalizeLogin(voterLogin);
        return !CommunityInput.IsValidLogin(login)
            ? Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.Invalid("A valid Twitch login is required.")
            )
            : await ExecuteWithCollisionRecoveryAsync(
                RetryGateFor(_voteRetryGates, HashCode.Combine(hostId, submissionId, login)),
                () => VoteAttemptAsync(hostId, submissionId, login, ct),
                () => LoadCommittedVoteRetryAsync(hostId, submissionId, login, ct),
                ct
            );
    }

    private async Task<RequestBoardResult<PublicRequestSubmissionView>> VoteAttemptAsync(
        int hostId,
        long submissionId,
        string login,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var submission = await db
            .RequestSubmissions.Include(value => value.Board)
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(value => value.Id == submissionId && value.HostId == hostId, ct);
        if (submission?.Board is null)
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.NotFound("Request not found.")
            );
        }

        if (
            await db.RequestSubmissionVotes.AnyAsync(
                vote => vote.SubmissionId == submissionId && vote.VoterLogin == login,
                ct
            )
        )
        {
            return new RequestBoardResult<PublicRequestSubmissionView>.Succeeded(
                ToPublicView(submission),
                true
            );
        }

        if (
            !submission.Board.VotingEnabled
            || submission.Status
                is not (
                    RequestSubmissionStatus.Approved
                    or RequestSubmissionStatus.Queued
                    or RequestSubmissionStatus.Accepted
                )
        )
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.Conflict("Voting is not open for this request.")
            );
        }

        var voteCount = await db.RequestSubmissionVotes.CountAsync(
            vote => vote.Submission!.BoardId == submission.BoardId && vote.VoterLogin == login,
            ct
        );
        if (voteCount >= submission.Board.VoteLimitPerUser)
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.LimitReached(
                    $"This board allows {submission.Board.VoteLimitPerUser} votes per viewer."
                )
            );
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        _ = db.RequestSubmissionVotes.Add(
            new RequestSubmissionVote
            {
                SubmissionId = submission.Id,
                VoterLogin = login,
                CreatedAtUtc = now,
            }
        );
        submission.VoteCount++;
        submission.UpdatedAtUtc = now;
        AddEvent(
            db,
            submission.Board,
            submission.Id,
            RequestBoardEventKind.Voted,
            new { submission.Id, submission.VoteCount },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _ = await events.PublishAsync(AppEventKind.RequestBoardsChanged, ct);
        return Succeeded(ToPublicView(submission));
    }

    public async Task<RequestBoardResult<ModeratorRequestSubmissionView>> ModerateAsync(
        int hostId,
        ModerateRequestCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<ModeratorRequestSubmissionView>(
                new RequestBoardRejection.FeatureDisabled()
            );
        }

        var validation = ValidateModeration(command);
        if (validation is not null)
        {
            return Rejected<ModeratorRequestSubmissionView>(validation);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var submission = await db
            .RequestSubmissions.Include(value => value.Board)
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(
                value => value.Id == command.SubmissionId && value.HostId == hostId,
                ct
            );
        if (submission?.Board is null)
        {
            return Rejected<ModeratorRequestSubmissionView>(
                new RequestBoardRejection.NotFound("Request not found for the selected host.")
            );
        }

        if (submission.Status == command.TargetStatus)
        {
            return new RequestBoardResult<ModeratorRequestSubmissionView>.Succeeded(
                await ToModeratorViewAsync(db, submission, ct),
                true
            );
        }

        if (!CanTransition(submission.Status, command.TargetStatus))
        {
            return Rejected<ModeratorRequestSubmissionView>(
                new RequestBoardRejection.Conflict(
                    $"A {submission.Status} request cannot become {command.TargetStatus}."
                )
            );
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (command.TargetStatus == RequestSubmissionStatus.Queued && submission.QueuePosition == 0)
        {
            submission.QueuePosition =
                (
                    await db
                        .RequestSubmissions.Where(value => value.BoardId == submission.BoardId)
                        .MaxAsync(value => (long?)value.QueuePosition, ct)
                    ?? 0
                ) + 1;
        }

        submission.Status = command.TargetStatus;
        submission.PublicNote = command.PublicNote.Trim();
        submission.PrivateModeratorNote = command.PrivateModeratorNote.Trim();
        submission.PrivateRejectionReason = command.PrivateRejectionReason.Trim();
        submission.Priority = command.Priority;
        submission.Category = command.Category.Trim();
        submission.Tags = RequestBoardInput.JoinTags(NormalizeTags(command.Tags));
        submission.UpdatedAtUtc = now;
        if (command.TargetStatus == RequestSubmissionStatus.Completed)
        {
            submission.PointReservationState =
                submission.PointReservationState == RequestPointReservationState.Reserved
                    ? RequestPointReservationState.Consumed
                    : submission.PointReservationState;
        }
        else if (command.TargetStatus == RequestSubmissionStatus.Rejected)
        {
            await RefundIfRequiredAsync(db, submission, RequestClosure.Rejected, now, ct);
        }

        AddEvent(
            db,
            submission.Board,
            submission.Id,
            RequestBoardEventKind.StatusChanged,
            new
            {
                submission.Id,
                Status = PersistedEnumTokens<RequestSubmissionStatus>.Format(submission.Status),
                submission.PublicNote,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _ = await events.PublishAsync(AppEventKind.RequestBoardsChanged, ct);
        return Succeeded(await ToModeratorViewAsync(db, submission, CancellationToken.None));
    }

    public async Task<RequestBoardResult<PublicRequestSubmissionView>> WithdrawAsync(
        int hostId,
        long submissionId,
        string submitterLogin,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.FeatureDisabled()
            );
        }

        var login = CommunityInput.NormalizeLogin(submitterLogin);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var submission = await db
            .RequestSubmissions.Include(value => value.Board)
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(
                value =>
                    value.Id == submissionId
                    && value.HostId == hostId
                    && value.SubmitterLogin == login,
                ct
            );
        if (submission?.Board is null)
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.NotFound("Request not found.")
            );
        }

        if (submission.Status == RequestSubmissionStatus.Withdrawn)
        {
            return new RequestBoardResult<PublicRequestSubmissionView>.Succeeded(
                ToPublicView(submission),
                true
            );
        }

        if (
            submission.Status
            is not (
                RequestSubmissionStatus.Pending
                or RequestSubmissionStatus.Approved
                or RequestSubmissionStatus.Queued
            )
        )
        {
            return Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.Conflict(
                    "Only pending, approved, or queued requests can be withdrawn."
                )
            );
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        submission.Status = RequestSubmissionStatus.Withdrawn;
        submission.UpdatedAtUtc = now;
        await RefundIfRequiredAsync(db, submission, RequestClosure.Withdrawn, now, ct);
        AddEvent(
            db,
            submission.Board,
            submission.Id,
            RequestBoardEventKind.StatusChanged,
            new { submission.Id, Status = "Withdrawn" },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _ = await events.PublishAsync(AppEventKind.RequestBoardsChanged, ct);
        return Succeeded(ToPublicView(submission));
    }

    public async Task<RequestBoardResult<ModeratorRequestSubmissionView>> MergeAsync(
        int hostId,
        long sourceSubmissionId,
        long targetSubmissionId,
        string publicNote,
        string privateModeratorNote,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<ModeratorRequestSubmissionView>(
                new RequestBoardRejection.FeatureDisabled()
            );
        }

        if (sourceSubmissionId == targetSubmissionId)
        {
            return Rejected<ModeratorRequestSubmissionView>(
                new RequestBoardRejection.Invalid("A request cannot be merged into itself.")
            );
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var submissions = await db
            .RequestSubmissions.Include(value => value.Board)
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .Where(value =>
                value.HostId == hostId
                && (value.Id == sourceSubmissionId || value.Id == targetSubmissionId)
            )
            .ToListAsync(ct);
        var source = submissions.SingleOrDefault(value => value.Id == sourceSubmissionId);
        var target = submissions.SingleOrDefault(value => value.Id == targetSubmissionId);
        if (source?.Board is null || target is null || source.BoardId != target.BoardId)
        {
            return Rejected<ModeratorRequestSubmissionView>(
                new RequestBoardRejection.NotFound(
                    "Both requests must exist on the same board and selected host."
                )
            );
        }

        if (source.Status == RequestSubmissionStatus.Merged)
        {
            return source.MergedIntoSubmissionId != target.Id
                ? Rejected<ModeratorRequestSubmissionView>(
                    new RequestBoardRejection.Conflict(
                        $"This request was already merged into request #{source.MergedIntoSubmissionId}."
                    )
                )
                : new RequestBoardResult<ModeratorRequestSubmissionView>.Succeeded(
                    await ToModeratorViewAsync(db, source, ct),
                    true
                );
        }

        if (!ActiveSubmissionStatuses().Contains(source.Status))
        {
            return Rejected<ModeratorRequestSubmissionView>(
                new RequestBoardRejection.Conflict("Only an active request can be merged.")
            );
        }

        var sourceVotes = await db
            .RequestSubmissionVotes.Where(vote => vote.SubmissionId == source.Id)
            .ToListAsync(ct);
        var targetVoters = await db
            .RequestSubmissionVotes.Where(vote => vote.SubmissionId == target.Id)
            .Select(vote => vote.VoterLogin)
            .ToHashSetAsync(StringComparer.Ordinal, ct);
        foreach (var vote in sourceVotes)
        {
            if (targetVoters.Add(vote.VoterLogin))
            {
                vote.SubmissionId = target.Id;
                target.VoteCount++;
            }
            else
            {
                _ = db.RequestSubmissionVotes.Remove(vote);
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        source.VoteCount = 0;
        source.Status = RequestSubmissionStatus.Merged;
        source.MergedIntoSubmissionId = target.Id;
        source.PublicNote = publicNote.Trim();
        source.PrivateModeratorNote = privateModeratorNote.Trim();
        source.UpdatedAtUtc = now;
        target.UpdatedAtUtc = now;
        await RefundIfRequiredAsync(db, source, RequestClosure.Merged, now, ct);
        AddEvent(
            db,
            source.Board,
            source.Id,
            RequestBoardEventKind.Merged,
            new
            {
                SourceSubmissionId = source.Id,
                TargetSubmissionId = target.Id,
                source.PublicNote,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _ = await events.PublishAsync(AppEventKind.RequestBoardsChanged, ct);
        return Succeeded(await ToModeratorViewAsync(db, source, CancellationToken.None));
    }

    public async Task<RequestBoardPage?> GetPublicPageAsync(
        string hostLogin,
        string boardSlug,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostLogin, ct))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalizedHost = CommunityInput.NormalizeLogin(hostLogin);
        var normalizedSlug = CommunityInput.NormalizeSlug(boardSlug);
        var board = await db
            .RequestBoards.AsNoTracking()
            .Include(value => value.Fields)
            .SingleOrDefaultAsync(
                value =>
                    value.Slug == normalizedSlug
                    && value.HostId
                        == db.Hosts.Where(host => host.Login == normalizedHost)
                            .Select(host => host.Id)
                            .SingleOrDefault(),
                ct
            );
        if (board is null)
        {
            return null;
        }

        var host = await db.Hosts.AsNoTracking().SingleAsync(value => value.Id == board.HostId, ct);
        var submissions = await db
            .RequestSubmissions.AsNoTracking()
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .Where(value => value.BoardId == board.Id)
            .ToListAsync(ct);
        return new RequestBoardPage(
            ToSummary(board, host.Login),
            OrderSubmissions(submissions).Select(ToPublicView).ToArray()
        );
    }

    public async Task<IReadOnlyList<RequestBoardSummary>> GetBoardsForHostAsync(
        int hostId,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == hostId, ct);
        if (host is null)
        {
            return [];
        }

        var boards = await db
            .RequestBoards.AsNoTracking()
            .Include(value => value.Fields)
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Title)
            .ToListAsync(ct);
        return boards.Select(value => ToSummary(value, host.Login)).ToArray();
    }

    public async Task<RequestBoardModeratorPage?> GetModeratorPageAsync(
        int hostId,
        string boardSlug,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalizedSlug = CommunityInput.NormalizeSlug(boardSlug);
        var board = await db
            .RequestBoards.AsNoTracking()
            .Include(value => value.Fields)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.Slug == normalizedSlug,
                ct
            );
        if (board is null)
        {
            return null;
        }

        var host = await db.Hosts.AsNoTracking().SingleAsync(value => value.Id == hostId, ct);
        var submissions = await db
            .RequestSubmissions.AsNoTracking()
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .Where(value => value.BoardId == board.Id)
            .ToListAsync(ct);
        var views = new List<ModeratorRequestSubmissionView>(submissions.Count);
        foreach (var submission in OrderSubmissions(submissions))
        {
            views.Add(await ToModeratorViewAsync(db, submission, ct));
        }

        return new RequestBoardModeratorPage(ToSummary(board, host.Login), views);
    }

    public async Task<ModeratorRequestSubmissionView?> GetModeratorSubmissionAsync(
        int hostId,
        long submissionId,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db
            .RequestSubmissions.AsNoTracking()
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(value => value.HostId == hostId && value.Id == submissionId, ct);
        return submission is null ? null : await ToModeratorViewAsync(db, submission, ct);
    }

    public async Task<IReadOnlyList<RequestBoardEventView>> GetEventsAsync(
        int hostId,
        long afterEventId,
        int count,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return [];
        }

        var boundedCount = Math.Clamp(count, 1, _maximumEventReadCount);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .RequestBoardEvents.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Id > afterEventId)
            .OrderBy(value => value.Id)
            .Take(boundedCount)
            .Select(value => new RequestBoardEventView(
                value.Id,
                value.HostId,
                value.BoardId,
                value.SubmissionId,
                value.SchemaVersion,
                value.Kind,
                value.PublicPayload,
                value.OccurredAtUtc
            ))
            .ToListAsync(ct);
    }

    private async Task<RequestBoardResult<PublicRequestSubmissionView>?> LoadCommittedSubmissionRetryAsync(
        int hostId,
        string boardSlug,
        Guid operationId,
        string submitterLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db
            .RequestSubmissions.AsNoTracking()
            .Include(value => value.Board)
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.OperationId == operationId,
                ct
            );
        return submission switch
        {
            null => null,
            { } value
                when value.Board?.Slug != CommunityInput.NormalizeSlug(boardSlug)
                    || !string.Equals(
                        value.SubmitterLogin,
                        submitterLogin,
                        StringComparison.Ordinal
                    ) => Rejected<PublicRequestSubmissionView>(
                new RequestBoardRejection.Conflict(
                    "That operation ID belongs to another submission."
                )
            ),
            { } value => new RequestBoardResult<PublicRequestSubmissionView>.Succeeded(
                ToPublicView(value),
                true
            ),
        };
    }

    private async Task<RequestBoardResult<PublicRequestSubmissionView>?> LoadCommittedVoteRetryAsync(
        int hostId,
        long submissionId,
        string voterLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db
            .RequestSubmissions.AsNoTracking()
            .Include(value => value.Board)
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(value => value.Id == submissionId && value.HostId == hostId, ct);
        return
            submission?.Board is null
            || !await db.RequestSubmissionVotes.AnyAsync(
                vote => vote.SubmissionId == submissionId && vote.VoterLogin == voterLogin,
                ct
            )
            ? null
            : (RequestBoardResult<PublicRequestSubmissionView>)
                new RequestBoardResult<PublicRequestSubmissionView>.Succeeded(
                    ToPublicView(submission),
                    true
                );
    }

    private async Task RefundIfRequiredAsync(
        BlokeBotDbContext db,
        RequestSubmission submission,
        RequestClosure closure,
        DateTime now,
        CancellationToken ct
    )
    {
        if (
            submission.PointReservationState != RequestPointReservationState.Reserved
            || submission.Board is null
            || !ShouldRefund(submission.Board.RefundPolicy, closure)
        )
        {
            return;
        }

        var amount = PointAmount.ParseAbsolute(submission.Board.PointCost);
        var balance = await LoadBalanceAsync(
            db,
            submission.HostId,
            submission.SubmitterLogin,
            now,
            ct
        );
        var current = PointAmount.ParseAbsolute(balance.Amount);
        var next = current.Add(amount);
        balance.Amount = next.ToString();
        balance.UpdatedAtUtc = now;
        submission.PointReservationState = RequestPointReservationState.Refunded;
        AddPointLedger(
            db,
            submission,
            PointLedgerKind.RequestRefund,
            amount.Value,
            next,
            "Request-board point refund",
            now
        );
        AddEvent(
            db,
            submission.Board,
            submission.Id,
            RequestBoardEventKind.PointsRefunded,
            new { submission.Id, Amount = amount.ToString() },
            now
        );
    }

    private Task<bool> FeatureIsEnabledAsync(int hostId, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(
            dbFactory,
            hostId,
            HostFeatureFlags.RequestBoards,
            ct
        );

    private Task<bool> FeatureIsEnabledAsync(string hostLogin, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(
            dbFactory,
            hostLogin,
            HostFeatureFlags.RequestBoards,
            ct
        );

    private static bool ShouldRefund(RequestBoardRefundPolicy policy, RequestClosure closure) =>
        policy switch
        {
            RequestBoardRefundPolicy.Never => false,
            RequestBoardRefundPolicy.RejectedOrWithdrawn => closure
                is RequestClosure.Rejected
                    or RequestClosure.Withdrawn,
            RequestBoardRefundPolicy.AnyUnfulfilledClosure => true,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null),
        };

    private static async Task<PointBalance> LoadBalanceAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        DateTime now,
        CancellationToken ct
    )
    {
        var balance = await db.PointBalances.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.Login == login,
            ct
        );
        if (balance is not null)
        {
            return balance;
        }

        balance = new PointBalance
        {
            HostId = hostId,
            Login = login,
            Amount = "0",
            UpdatedAtUtc = now,
        };
        _ = db.PointBalances.Add(balance);
        return balance;
    }

    private static void AddPointLedger(
        BlokeBotDbContext db,
        RequestSubmission submission,
        PointLedgerKind kind,
        BigInteger delta,
        PointAmount balanceAfter,
        string note,
        DateTime now
    ) =>
        db.PointLedgerEntries.Add(
            new PointLedgerEntry
            {
                HostId = submission.HostId,
                CreatedAtUtc = now,
                Kind = kind,
                Login = submission.SubmitterLogin,
                Delta = delta.ToString(CultureInfo.InvariantCulture),
                BalanceAfter = balanceAfter.ToString(),
                ActorLogin = submission.SubmitterLogin,
                RequestSubmissionId = submission.Id,
                Note = note,
            }
        );

    private static void AddEvent(
        BlokeBotDbContext db,
        RequestBoard board,
        long? submissionId,
        RequestBoardEventKind kind,
        object publicPayload,
        DateTime occurredAtUtc
    )
    {
        var payload = JsonSerializer.Serialize(publicPayload);
        if (payload.Length > _maximumEventPayloadLength)
        {
            payload = """{"truncated":true}""";
        }

        _ = db.RequestBoardEvents.Add(
            new RequestBoardDomainEvent
            {
                HostId = board.HostId,
                BoardId = board.Id,
                SubmissionId = submissionId,
                SchemaVersion = _eventSchemaVersion,
                Kind = kind,
                PublicPayload = payload,
                OccurredAtUtc = occurredAtUtc,
            }
        );
    }

    private static RequestBoardRejection? ValidateConfiguration(
        int hostId,
        ConfigureRequestBoardCommand command
    )
    {
        var slug = CommunityInput.NormalizeSlug(command.Slug);
        if (hostId <= 0)
        {
            return new RequestBoardRejection.Invalid("A host is required.");
        }

        if (!CommunityInput.IsValidSlug(slug))
        {
            return new RequestBoardRejection.Invalid(
                "Board slug must be 1-48 lowercase letters, numbers, or hyphens."
            );
        }

        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Trim().Length > 100)
        {
            return new RequestBoardRejection.Invalid(
                "Board title must be between 1 and 100 characters."
            );
        }

        if (command.Description.Trim().Length > 1000)
        {
            return new RequestBoardRejection.Invalid(
                "Board description cannot exceed 1000 characters."
            );
        }

        if (
            !BigInteger.TryParse(
                command.PointCost,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var cost
            )
            || cost < BigInteger.Zero
            || cost > PointAmount.MaximumValue
        )
        {
            return new RequestBoardRejection.Invalid(
                "Point cost must be a whole number from 0 to 10^100."
            );
        }

        if (
            command.SubmissionLimitPerUser is < 1 or > 100
            || command.VoteLimitPerUser is < 1 or > 1000
            || command.SubmissionCooldownSeconds is < 0 or > 2_592_000
        )
        {
            return new RequestBoardRejection.Invalid(
                "Submission limit, vote limit, or cooldown is outside its supported range."
            );
        }

        if (command.Fields.Count is < 1 or > RequestBoardLimits.MaximumFields)
        {
            return new RequestBoardRejection.Invalid(
                $"A board must define 1-{RequestBoardLimits.MaximumFields} fields."
            );
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in command.Fields)
        {
            var key = CommunityInput.NormalizeSlug(field.Key);
            if (
                !CommunityInput.IsValidSlug(key)
                || !keys.Add(key)
                || string.IsNullOrWhiteSpace(field.Label)
                || field.Label.Trim().Length > 100
            )
            {
                return new RequestBoardRejection.Invalid(
                    "Every field needs a unique valid key and a label up to 100 characters."
                );
            }

            if (
                field.Kind is RequestBoardFieldKind.Text or RequestBoardFieldKind.Url
                && field.MaximumLength is < 1 or > 2048
            )
            {
                return new RequestBoardRejection.Invalid(
                    "Text and URL field limits must be between 1 and 2048 characters."
                );
            }

            if (
                field.Kind == RequestBoardFieldKind.Number
                && field.MinimumNumber is { } minimum
                && field.MaximumNumber is { } maximum
                && minimum > maximum
            )
            {
                return new RequestBoardRejection.Invalid(
                    "A number field minimum cannot exceed its maximum."
                );
            }

            var choices = field.Choices ?? [];
            if (
                field.Kind == RequestBoardFieldKind.Choice
                && (
                    choices.Count is < 1 or > RequestBoardLimits.MaximumChoices
                    || choices.Any(static value =>
                        string.IsNullOrWhiteSpace(value)
                        || value.Length > 100
                        || value.Contains('\n', StringComparison.Ordinal)
                    )
                    || choices.Distinct(StringComparer.OrdinalIgnoreCase).Count() != choices.Count
                )
            )
            {
                return new RequestBoardRejection.Invalid(
                    $"Choice fields require 1-{RequestBoardLimits.MaximumChoices} unique bounded choices."
                );
            }
        }

        return null;
    }

    private static ValidatedSubmission ValidateSubmission(
        RequestBoard board,
        SubmitRequestCommand command
    )
    {
        var title = command.Title.Trim();
        if (title.Length is < 1 or > 200 || RequestBoardInput.NormalizeTitle(title).Length == 0)
        {
            return ValidatedSubmission.Invalid(
                new RequestBoardRejection.Invalid(
                    "Request title must be between 1 and 200 characters."
                )
            );
        }

        var category = command.Category.Trim();
        if (category.Length > 64)
        {
            return ValidatedSubmission.Invalid(
                new RequestBoardRejection.Invalid("Category cannot exceed 64 characters.")
            );
        }

        var tags = NormalizeTags(command.Tags);
        if (
            tags.Count > RequestBoardLimits.MaximumTags
            || tags.Any(tag => tag.Length is < 1 or > 32)
        )
        {
            return ValidatedSubmission.Invalid(
                new RequestBoardRejection.Invalid(
                    $"Use at most {RequestBoardLimits.MaximumTags} tags of 1-32 characters."
                )
            );
        }

        var knownKeys = board.Fields.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        if (command.FieldValues.Keys.Any(key => !knownKeys.Contains(key)))
        {
            return ValidatedSubmission.Invalid(
                new RequestBoardRejection.Invalid("The submission contains an unknown field.")
            );
        }

        var values = new Dictionary<int, string>();
        string? normalizedUrl = null;
        foreach (var field in board.Fields)
        {
            _ = command.FieldValues.TryGetValue(field.Key, out var rawValue);
            var value = (rawValue ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                if (field.IsRequired)
                {
                    return ValidatedSubmission.Invalid(
                        new RequestBoardRejection.Invalid($"{field.Label} is required.")
                    );
                }

                continue;
            }

            switch (field.Kind)
            {
                case RequestBoardFieldKind.Text:
                    if (value.Length > field.MaximumLength)
                    {
                        return InvalidField(
                            field,
                            $"cannot exceed {field.MaximumLength} characters"
                        );
                    }
                    break;
                case RequestBoardFieldKind.Url:
                case RequestBoardFieldKind.TwitchClip:
                    if (
                        value.Length > field.MaximumLength
                        || !RequestBoardInput.TryNormalizeUrl(value, out var parsedUrl)
                        || (
                            field.Kind == RequestBoardFieldKind.TwitchClip
                            && !RequestBoardInput.IsTwitchClipUrl(parsedUrl)
                        )
                    )
                    {
                        return InvalidField(
                            field,
                            field.Kind == RequestBoardFieldKind.TwitchClip
                                ? "must be a valid Twitch clip URL"
                                : "must be a valid HTTP or HTTPS URL"
                        );
                    }

                    value = parsedUrl;
                    normalizedUrl ??= parsedUrl;
                    break;
                case RequestBoardFieldKind.Choice:
                    var choices = ParseChoices(field.ChoiceOptions);
                    var choice = choices.FirstOrDefault(option =>
                        string.Equals(option, value, StringComparison.OrdinalIgnoreCase)
                    );
                    if (choice is null)
                    {
                        return InvalidField(field, "must be one of the configured choices");
                    }

                    value = choice;
                    break;
                case RequestBoardFieldKind.Number:
                    if (
                        !decimal.TryParse(
                            value,
                            NumberStyles.Number,
                            CultureInfo.InvariantCulture,
                            out var number
                        )
                        || (field.MinimumNumber is { } minimum && number < minimum)
                        || (field.MaximumNumber is { } maximum && number > maximum)
                    )
                    {
                        return InvalidField(field, "must be a number inside the configured range");
                    }

                    value = number.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field.Kind), field.Kind, null);
            }

            values.Add(field.Id, value);
        }

        return new ValidatedSubmission(
            title,
            RequestBoardInput.NormalizeTitle(title),
            normalizedUrl,
            category,
            tags,
            values,
            null
        );
    }

    private static ValidatedSubmission InvalidField(RequestBoardField field, string detail) =>
        ValidatedSubmission.Invalid(new RequestBoardRejection.Invalid($"{field.Label} {detail}."));

    private static RequestBoardRejection? ValidateModeration(ModerateRequestCommand command)
    {
        if (
            command.TargetStatus
            is RequestSubmissionStatus.Pending
                or RequestSubmissionStatus.Withdrawn
                or RequestSubmissionStatus.Merged
        )
        {
            return new RequestBoardRejection.Invalid("That status is not a moderator transition.");
        }

        if (
            command.PublicNote.Trim().Length > 500
            || command.PrivateModeratorNote.Trim().Length > 1000
            || command.PrivateRejectionReason.Trim().Length > 1000
            || command.Category.Trim().Length > 64
            || command.Priority is < -1000 or > 1000
        )
        {
            return new RequestBoardRejection.Invalid(
                "Moderation metadata exceeds its supported bounds."
            );
        }

        var tags = NormalizeTags(command.Tags);
        return
            tags.Count > RequestBoardLimits.MaximumTags
            || tags.Any(static value => value.Length is < 1 or > 32)
            ? new RequestBoardRejection.Invalid("Moderation tags exceed their supported bounds.")
            : null;
    }

    private static bool CanTransition(
        RequestSubmissionStatus current,
        RequestSubmissionStatus target
    ) =>
        (current, target) switch
        {
            (RequestSubmissionStatus.Pending, RequestSubmissionStatus.Approved) => true,
            (RequestSubmissionStatus.Pending, RequestSubmissionStatus.Rejected) => true,
            (RequestSubmissionStatus.Approved, RequestSubmissionStatus.Queued) => true,
            (RequestSubmissionStatus.Approved, RequestSubmissionStatus.Accepted) => true,
            (RequestSubmissionStatus.Approved, RequestSubmissionStatus.Rejected) => true,
            (RequestSubmissionStatus.Queued, RequestSubmissionStatus.Accepted) => true,
            (RequestSubmissionStatus.Queued, RequestSubmissionStatus.Completed) => true,
            (RequestSubmissionStatus.Queued, RequestSubmissionStatus.Rejected) => true,
            (RequestSubmissionStatus.Accepted, RequestSubmissionStatus.Completed) => true,
            (RequestSubmissionStatus.Accepted, RequestSubmissionStatus.Rejected) => true,
            _ => false,
        };

    private static RequestSubmissionStatus[] ActiveSubmissionStatuses() =>
        [
            RequestSubmissionStatus.Pending,
            RequestSubmissionStatus.Approved,
            RequestSubmissionStatus.Queued,
            RequestSubmissionStatus.Accepted,
        ];

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(static value => value.Trim().ToLowerInvariant())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static int EffectiveMaximumLength(RequestBoardFieldCommand field) =>
        field.Kind switch
        {
            RequestBoardFieldKind.Text or RequestBoardFieldKind.Url => field.MaximumLength,
            RequestBoardFieldKind.TwitchClip => 2048,
            RequestBoardFieldKind.Choice => 100,
            RequestBoardFieldKind.Number => 128,
            _ => throw new ArgumentOutOfRangeException(nameof(field.Kind), field.Kind, null),
        };

    private static bool FieldShapeMatches(
        IReadOnlyCollection<RequestBoardField> existing,
        IReadOnlyList<RequestBoardFieldCommand> requested
    )
    {
        var left = existing.OrderBy(static value => value.Position).ToArray();
        return left.Length == requested.Count
            && left.Zip(requested)
                .All(static pair =>
                    pair.First.Key == CommunityInput.NormalizeSlug(pair.Second.Key)
                    && pair.First.Label == pair.Second.Label.Trim()
                    && pair.First.Kind == pair.Second.Kind
                    && pair.First.IsRequired == pair.Second.IsRequired
                    && pair.First.MaximumLength == EffectiveMaximumLength(pair.Second)
                    && pair.First.MinimumNumber == pair.Second.MinimumNumber
                    && pair.First.MaximumNumber == pair.Second.MaximumNumber
                    && ParseChoices(pair.First.ChoiceOptions)
                        .SequenceEqual(pair.Second.Choices ?? [], StringComparer.Ordinal)
                );
    }

    private static IReadOnlyList<string> ParseChoices(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<RequestSubmission> OrderSubmissions(
        IEnumerable<RequestSubmission> submissions
    ) =>
        submissions
            .OrderBy(static value => StatusOrder(value.Status))
            .ThenByDescending(static value => value.Priority)
            .ThenByDescending(static value => value.VoteCount)
            .ThenBy(static value => value.QueuePosition == 0 ? long.MaxValue : value.QueuePosition)
            .ThenBy(static value => value.CreatedAtUtc)
            .ThenBy(static value => value.Id);

    private static int StatusOrder(RequestSubmissionStatus status) =>
        status switch
        {
            RequestSubmissionStatus.Accepted => 0,
            RequestSubmissionStatus.Queued => 1,
            RequestSubmissionStatus.Approved => 2,
            RequestSubmissionStatus.Pending => 3,
            RequestSubmissionStatus.Completed => 4,
            RequestSubmissionStatus.Rejected => 5,
            RequestSubmissionStatus.Withdrawn => 6,
            RequestSubmissionStatus.Merged => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    private static PublicRequestSubmissionView ToPublicView(RequestSubmission submission) =>
        new PublicRequestSubmissionView(
            submission.Id,
            submission.SubmitterLogin,
            submission.Title,
            submission.Status,
            submission.Category,
            RequestBoardInput.ParseTags(submission.Tags),
            submission.Priority,
            submission.QueuePosition,
            submission.VoteCount,
            submission.PublicNote,
            submission.MergedIntoSubmissionId,
            submission.CreatedAtUtc,
            submission
                .Values.OrderBy(static value => value.Field?.Position ?? int.MaxValue)
                .Where(static value => value.Field is not null)
                .Select(static value => new RequestFieldValueView(
                    value.Field!.Key,
                    value.Field.Label,
                    value.Field.Kind,
                    value.Value
                ))
                .ToArray()
        );

    private static async Task<ModeratorRequestSubmissionView> ToModeratorViewAsync(
        BlokeBotDbContext db,
        RequestSubmission submission,
        CancellationToken ct
    )
    {
        var duplicates = await db
            .RequestSubmissions.AsNoTracking()
            .Where(value =>
                value.BoardId == submission.BoardId
                && value.Id != submission.Id
                && value.Status != RequestSubmissionStatus.Merged
                && (
                    value.NormalizedTitle == submission.NormalizedTitle
                    || (
                        submission.NormalizedUrl != null
                        && value.NormalizedUrl == submission.NormalizedUrl
                    )
                )
            )
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .Take(10)
            .ToListAsync(ct);
        return new ModeratorRequestSubmissionView(
            ToPublicView(submission),
            submission.PrivateModeratorNote,
            submission.PrivateRejectionReason,
            submission.PointReservationState,
            duplicates
        );
    }

    private static RequestBoardSummary ToSummary(RequestBoard board, string hostLogin) =>
        new RequestBoardSummary(
            board.Id,
            board.HostId,
            hostLogin,
            board.Slug,
            board.Title,
            board.Description,
            board.IsOpen,
            board.PointCost,
            board.RefundPolicy,
            board.SubmissionLimitPerUser,
            board.SubmissionCooldownSeconds,
            board.VoteLimitPerUser,
            board.VotingEnabled,
            RequestBoard.DefaultOrderingDescription,
            board
                .Fields.OrderBy(static value => value.Position)
                .Select(static value => new RequestBoardFieldView(
                    value.Id,
                    value.Key,
                    value.Label,
                    value.Kind,
                    value.IsRequired,
                    value.MaximumLength,
                    value.MinimumNumber,
                    value.MaximumNumber,
                    ParseChoices(value.ChoiceOptions)
                ))
                .ToArray()
        );

    private static async Task<RequestBoardSummary> LoadSummaryAsync(
        BlokeBotDbContext db,
        RequestBoard board,
        CancellationToken ct
    )
    {
        var hostLogin = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == board.HostId)
            .Select(value => value.Login)
            .SingleAsync(ct);
        return ToSummary(board, hostLogin);
    }

    private static SemaphoreSlim[] CreateRetryGates() =>
        Enumerable.Range(0, _retryGateCount).Select(static _ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim RetryGateFor(SemaphoreSlim[] gates, int hash) =>
        gates[(int)((uint)hash % (uint)gates.Length)];

    private static async Task<T> ExecuteWithCollisionRecoveryAsync<T>(
        SemaphoreSlim gate,
        Func<Task<T>> attempt,
        Func<Task<T?>> loadCommitted,
        CancellationToken ct
    )
        where T : class
    {
        await gate.WaitAsync(ct);
        try
        {
            try
            {
                return await attempt();
            }
            catch (Exception exception) when (IsRetryCollision(exception))
            {
                var committed = await loadCommitted();
                if (committed is not null)
                {
                    return committed;
                }

                throw;
            }
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private static bool IsRetryCollision(Exception exception) =>
        MainDatabaseFailureClassifier.IsRetryableTransactionContention(exception);

    private static RequestBoardResult<T> Succeeded<T>(T value) =>
        new RequestBoardResult<T>.Succeeded(value);

    private static RequestBoardResult<T> Rejected<T>(RequestBoardRejection rejection) =>
        new RequestBoardResult<T>.Rejected(rejection);

    private sealed record ValidatedSubmission(
        string Title,
        string NormalizedTitle,
        string? NormalizedUrl,
        string Category,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<int, string> Values,
        RequestBoardRejection? Rejection
    )
    {
        public static ValidatedSubmission Invalid(RequestBoardRejection rejection) =>
            new(
                string.Empty,
                string.Empty,
                null,
                string.Empty,
                [],
                new Dictionary<int, string>(),
                rejection
            );
    }

    private enum RequestClosure
    {
        Rejected,
        Withdrawn,
        Merged,
    }
}
