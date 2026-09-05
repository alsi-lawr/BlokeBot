using System.Globalization;
using System.Text;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.RequestBoards;

public static class RequestBoardLimits
{
    public const int MaximumFields = 12;
    public const int MaximumChoices = 20;
    public const int MaximumTags = 10;
    public const int MaximumPublicPageSize = 100;
}

public sealed record RequestBoardFieldCommand(
    string Key,
    string Label,
    RequestBoardFieldKind Kind,
    bool IsRequired,
    int MaximumLength,
    decimal? MinimumNumber = null,
    decimal? MaximumNumber = null,
    IReadOnlyList<string>? Choices = null
);

public sealed record ConfigureRequestBoardCommand(
    string Slug,
    string Title,
    string Description,
    bool IsOpen,
    string PointCost,
    RequestBoardRefundPolicy RefundPolicy,
    int SubmissionLimitPerUser,
    int SubmissionCooldownSeconds,
    int VoteLimitPerUser,
    bool VotingEnabled,
    IReadOnlyList<RequestBoardFieldCommand> Fields
);

public sealed record SubmitRequestCommand(
    Guid OperationId,
    RequestActor Actor,
    string Title,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> FieldValues
);

public sealed record ModerateRequestCommand(
    long SubmissionId,
    RequestSubmissionStatus TargetStatus,
    string PublicNote,
    string PrivateModeratorNote,
    string PrivateRejectionReason,
    int Priority,
    string Category,
    IReadOnlyList<string> Tags
);

public sealed record RequestBoardFieldView(
    int Id,
    string Key,
    string Label,
    RequestBoardFieldKind Kind,
    bool IsRequired,
    int MaximumLength,
    decimal? MinimumNumber,
    decimal? MaximumNumber,
    IReadOnlyList<string> Choices
);

public sealed record RequestBoardSummary(
    int Id,
    int HostId,
    string HostLogin,
    string Slug,
    string Title,
    string Description,
    bool IsOpen,
    string PointCost,
    RequestBoardRefundPolicy RefundPolicy,
    int SubmissionLimitPerUser,
    int SubmissionCooldownSeconds,
    int VoteLimitPerUser,
    bool VotingEnabled,
    string OrderingDescription,
    IReadOnlyList<RequestBoardFieldView> Fields
);

public sealed record RequestFieldValueView(
    string Key,
    string Label,
    RequestBoardFieldKind Kind,
    string Value
);

public sealed record PublicRequestSubmissionView(
    long Id,
    string SubmitterLogin,
    string Title,
    RequestSubmissionStatus Status,
    string Category,
    IReadOnlyList<string> Tags,
    int Priority,
    long QueuePosition,
    int VoteCount,
    string PublicNote,
    long? MergedIntoSubmissionId,
    DateTime CreatedAtUtc,
    IReadOnlyList<RequestFieldValueView> Values
);

public sealed record ModeratorRequestSubmissionView(
    PublicRequestSubmissionView Public,
    string PrivateModeratorNote,
    string PrivateRejectionReason,
    RequestPointReservationState PointReservationState,
    IReadOnlyList<long> PossibleDuplicateIds
);

public sealed record RequestBoardPage(
    RequestBoardSummary Board,
    IReadOnlyList<PublicRequestSubmissionView> Submissions
);

public sealed record RequestBoardModeratorPage(
    RequestBoardSummary Board,
    IReadOnlyList<ModeratorRequestSubmissionView> Submissions
);

public sealed record RequestBoardEventView(
    long Id,
    int HostId,
    int BoardId,
    long? SubmissionId,
    int SchemaVersion,
    RequestBoardEventKind Kind,
    string PublicPayload,
    DateTime OccurredAtUtc
);

public abstract record RequestBoardResult<T>
{
    private RequestBoardResult() { }

    public abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Rejected, TResult> rejected
    );

    public sealed record Succeeded(T Value, bool WasIdempotent = false) : RequestBoardResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => succeeded(this);
    }

    public sealed record Rejected(RequestBoardRejection Reason) : RequestBoardResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => rejected(this);
    }
}

public abstract record RequestBoardRejection(string Message)
{
    public sealed record FeatureDisabled()
        : RequestBoardRejection("Request boards are turned off for this channel.");

    public sealed record Invalid(string Detail) : RequestBoardRejection(Detail);

    public sealed record NotFound(string Detail) : RequestBoardRejection(Detail);

    public sealed record Closed() : RequestBoardRejection("This request board is closed.");

    public sealed record LimitReached(string Detail) : RequestBoardRejection(Detail);

    public sealed record Cooldown(DateTime AvailableAtUtc)
        : RequestBoardRejection(
            $"Another request can be submitted after {AvailableAtUtc.ToString("u", CultureInfo.InvariantCulture)}."
        );

    public sealed record InsufficientPoints(string Cost)
        : RequestBoardRejection($"This request costs {Cost} points.");

    public sealed record Conflict(string Detail) : RequestBoardRejection(Detail);
}

internal static class RequestBoardInput
{
    public static string NormalizeTitle(string value)
    {
        var builder = new StringBuilder(value.Length);
        var needsSpace = false;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (needsSpace && builder.Length > 0)
                {
                    _ = builder.Append(' ');
                }

                _ = builder.Append(character);
                needsSpace = false;
            }
            else
            {
                needsSpace = true;
            }
        }

        return builder.ToString();
    }

    public static bool TryNormalizeUrl(string value, out string normalized)
    {
        normalized = string.Empty;
        if (
            value.Length > 2048
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
        )
        {
            return false;
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        if (
            (builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80)
            || (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443)
        )
        {
            builder.Port = -1;
        }

        normalized = builder.Uri.AbsoluteUri;
        return normalized.Length <= 2048;
    }

    public static bool IsTwitchClipUrl(string normalizedUrl)
    {
        var uri = new Uri(normalizedUrl);
        return string.Equals(uri.Host, "clips.twitch.tv", StringComparison.OrdinalIgnoreCase)
            || (
                (
                    string.Equals(uri.Host, "twitch.tv", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "www.twitch.tv", StringComparison.OrdinalIgnoreCase)
                ) && uri.AbsolutePath.Contains("/clip/", StringComparison.OrdinalIgnoreCase)
            );
    }

    public static IReadOnlyList<string> ParseTags(string value) =>
        value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static tag => tag.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static string JoinTags(IEnumerable<string> tags) => string.Join(",", tags);
}
