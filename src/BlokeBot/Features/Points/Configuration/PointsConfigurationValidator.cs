using BlokeBot.Commands;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Configuration;

public static class PointsConfigurationValidator
{
    public const int MinimumGiveawayCooldownSeconds = 300;

    public static Validation<
        PointsConfigurationSaveCommand,
        PointsConfigurationValidationError
    > Validate(PointsConfiguration draft)
    {
        var errors = new List<PointsConfigurationValidationError>();
        var minimumPayout = ParsePayout(draft.GiveawayMinimumPayout, minimum: true, errors);
        var maximumPayout = ParsePayout(draft.GiveawayMaximumPayout, minimum: false, errors);
        if (minimumPayout is { } minimum && maximumPayout is { } maximum)
        {
            if (minimum.Value > maximum.Value)
            {
                errors.Add(new PointsConfigurationValidationError.InvalidPayoutOrder());
            }

            if (minimum.Value % 10 != 0 || maximum.Value % 10 != 0)
            {
                errors.Add(new PointsConfigurationValidationError.InvalidPayoutIncrement());
            }
        }

        if (draft.GamblingWinRatePercent is < 0 or > 100)
        {
            errors.Add(new PointsConfigurationValidationError.InvalidGamblingWinRate());
        }

        if (!Enum.IsDefined(draft.GiveawayEligibility))
        {
            errors.Add(new PointsConfigurationValidationError.InvalidGiveawayEligibility());
        }

        var aliases = SnapshotAliases(draft.Aliases);
        var duplicateAlias = CommandAliasPolicy.FindDuplicateAlias(
            aliases
                .ToDrafts()
                .Select(alias => new BlokeBot.Commands.CommandAliasDraft<AppCommandKind>(
                    alias.Kind,
                    alias.Aliases
                ))
        );
        if (duplicateAlias is not null)
        {
            errors.Add(new PointsConfigurationValidationError.DuplicateAlias(duplicateAlias));
        }

        if (errors.Count > 0)
        {
            return Validation<
                PointsConfigurationSaveCommand,
                PointsConfigurationValidationError
            >.Invalid(errors[0], errors.Skip(1).ToArray());
        }

        return Validation<PointsConfigurationSaveCommand, PointsConfigurationValidationError>.Valid(
            new PointsConfigurationSaveCommand(
                NormalizePointLabel(draft.PointLabel),
                aliases,
                SnapshotReplies(draft.Replies),
                draft.ReplyDelivery.ToMap(),
                draft.WhisperResponsesEnabled,
                draft.GamblingWinRatePercent,
                Math.Max(0, draft.GamblingCooldownSeconds),
                Math.Max(1, draft.GiveawayDurationSeconds),
                minimumPayout!.Value,
                maximumPayout!.Value,
                Math.Max(1, draft.GiveawayWinnerCount),
                draft.GiveawayEligibility,
                Math.Max(MinimumGiveawayCooldownSeconds, draft.GiveawayCooldownSeconds)
            )
        );
    }

    private static PointAmount? ParsePayout(
        string value,
        bool minimum,
        ICollection<PointsConfigurationValidationError> errors
    )
    {
        return PointAmount
            .ParseNonNegativeAbsolute(value)
            .Match<PointAmount?>(
                amount => amount,
                error =>
                {
                    errors.Add(PayoutError(minimum, error));
                    return null;
                }
            );
    }

    private static PointsConfigurationValidationError PayoutError(
        bool minimum,
        PointAmountParseError error
    )
    {
        return (minimum, error) switch
        {
            (true, PointAmountParseError.AmountOutOfRange) =>
                new PointsConfigurationValidationError.MinimumPayoutOutOfRange(),
            (false, PointAmountParseError.AmountOutOfRange) =>
                new PointsConfigurationValidationError.MaximumPayoutOutOfRange(),
            (true, _) => new PointsConfigurationValidationError.InvalidMinimumPayout(),
            (false, _) => new PointsConfigurationValidationError.InvalidMaximumPayout(),
        };
    }

    private static string NormalizePointLabel(string pointLabel)
    {
        return string.IsNullOrWhiteSpace(pointLabel) ? "points" : pointLabel.Trim();
    }

    private static PointsCommandAliases SnapshotAliases(PointsCommandAliasEditor aliases)
    {
        return new(
            NormalizeAliases(aliases.PointsAliases),
            NormalizeAliases(aliases.GivePointsAliases),
            NormalizeAliases(aliases.AddPointsAliases),
            NormalizeAliases(aliases.RemovePointsAliases),
            NormalizeAliases(aliases.GambleAliases),
            NormalizeAliases(aliases.GiveawayAliases),
            NormalizeAliases(aliases.JoinAliases),
            NormalizeAliases(aliases.EndGiveawayAliases),
            NormalizeAliases(aliases.CancelGiveawayAliases)
        );
    }

    private static string NormalizeAliases(string aliases)
    {
        return string.Join(", ", CommandAliasNormalizer.Split(aliases));
    }

    private static PointsReplySettings SnapshotReplies(PointsReplySettingsEditor replies)
    {
        return new(
            replies.BalanceReply.Trim(),
            replies.OtherBalanceReply.Trim(),
            replies.TransferReply.Trim(),
            replies.AddReply.Trim(),
            replies.RemoveReply.Trim(),
            replies.InvalidAmountReply.Trim(),
            replies.InsufficientBalanceReply.Trim(),
            replies.ModeratorOnlyReply.Trim(),
            replies.GamblingWinReply.Trim(),
            replies.GamblingLoseReply.Trim(),
            replies.GiveawayStartedReply.Trim(),
            replies.GiveawayUpdateReply.Trim(),
            replies.GiveawayJoinedReply.Trim(),
            replies.GiveawayAlreadyJoinedReply.Trim(),
            replies.GiveawayEndedReply.Trim(),
            replies.GiveawayNoEntrantsReply.Trim(),
            replies.GiveawayCancelledReply.Trim(),
            replies.GiveawayAlreadyActiveReply.Trim(),
            replies.GiveawayNotActiveReply.Trim(),
            replies.GiveawayCooldownReply.Trim(),
            replies.StreamOfflineReply.Trim(),
            replies.NotEligibleReply.Trim(),
            replies.FollowerEligibilityUnavailableReply.Trim()
        );
    }
}

public abstract record PointsConfigurationValidationError
{
    private PointsConfigurationValidationError() { }

    public abstract string Message { get; }

    public sealed record InvalidMinimumPayout : PointsConfigurationValidationError
    {
        public override string Message => "Point amount must be a whole number.";
    }

    public sealed record MinimumPayoutOutOfRange : PointsConfigurationValidationError
    {
        public override string Message => "Point amounts cannot exceed 10^100.";
    }

    public sealed record InvalidMaximumPayout : PointsConfigurationValidationError
    {
        public override string Message => "Point amount must be a whole number.";
    }

    public sealed record MaximumPayoutOutOfRange : PointsConfigurationValidationError
    {
        public override string Message => "Point amounts cannot exceed 10^100.";
    }

    public sealed record InvalidPayoutOrder : PointsConfigurationValidationError
    {
        public override string Message =>
            "The smallest giveaway prize cannot be larger than the largest prize.";
    }

    public sealed record InvalidPayoutIncrement : PointsConfigurationValidationError
    {
        public override string Message => "Giveaway prizes must be multiples of 10.";
    }

    public sealed record InvalidGamblingWinRate : PointsConfigurationValidationError
    {
        public override string Message => "The chance of winning must be between 0% and 100%.";
    }

    public sealed record InvalidGiveawayEligibility : PointsConfigurationValidationError
    {
        public override string Message => "Choose who can enter the giveaway.";
    }

    public sealed record DuplicateAlias(string Alias) : PointsConfigurationValidationError
    {
        public override string Message => $"!{Alias} is entered more than once.";
    }
}
