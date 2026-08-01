using BlokeBot.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public static class GuessingConfigurationValidator
{
    private const int _nameMaxLength = 128;

    public static Validation<
        GuessingConfigurationSaveCommand,
        GuessingConfigurationValidationError
    > Validate(GuessingConfiguration draft)
    {
        var errors = new List<GuessingConfigurationValidationError>();
        var selected = ValidateProfileGraph(draft, errors);
        var profileName = RequiredName(draft.Profile.Name, profile: true, errors);
        if (profileName.Length > _nameMaxLength)
        {
            errors.Add(new GuessingConfigurationValidationError.ProfileNameTooLong());
        }

        var profileSlug = GuessRoundProfileSlug.FromName(profileName).Value;
        if (
            draft.Profiles.Any(profile =>
                profile.Id != draft.Profile.Id
                && GuessRoundProfileSlug.FromName(profile.Name).Value == profileSlug
            )
        )
        {
            errors.Add(new GuessingConfigurationValidationError.DuplicateProfileName());
        }

        var reward = ParseReward(draft.Profile.WinningGuessPointReward, errors);
        if (
            draft.Pin.Enabled
            && draft.Pin.DurationSeconds is { } duration
            && duration is < 30 or > 1800
        )
        {
            errors.Add(new GuessingConfigurationValidationError.InvalidPinDuration());
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
            errors.Add(new GuessingConfigurationValidationError.DuplicateAlias(duplicateAlias));
        }

        var options = SnapshotOptions(draft.Profile, errors);
        if (errors.Count > 0 || selected is null || reward is null)
        {
            return Validation<
                GuessingConfigurationSaveCommand,
                GuessingConfigurationValidationError
            >.Invalid(errors[0], errors.Skip(1).ToArray());
        }

        var hasOtherDefault = draft.Profiles.Any(profile =>
            profile.Id != draft.Profile.Id && profile.IsDefault
        );
        return Validation<
            GuessingConfigurationSaveCommand,
            GuessingConfigurationValidationError
        >.Valid(
            new GuessingConfigurationSaveCommand(
                draft.Profile.Id,
                draft.Profile.Revision,
                profileName,
                draft.Profile.IsDefault || !hasOtherDefault,
                reward.Value,
                aliases,
                SnapshotReplies(draft.Profile.Replies),
                draft.ReplyDelivery.ToMap(),
                draft.Pin,
                options
            )
        );
    }

    public static Validation<
        GuessingProfileCreateCommand,
        GuessingConfigurationValidationError
    > ValidateNewProfile(string name)
    {
        var errors = new List<GuessingConfigurationValidationError>();
        var normalizedName = RequiredName(name, profile: true, errors);
        if (normalizedName.Length > _nameMaxLength)
        {
            errors.Add(new GuessingConfigurationValidationError.ProfileNameTooLong());
        }

        return errors.Count > 0
            ? Validation<
                GuessingProfileCreateCommand,
                GuessingConfigurationValidationError
            >.Invalid(errors[0], errors.Skip(1).ToArray())
            : Validation<GuessingProfileCreateCommand, GuessingConfigurationValidationError>.Valid(
                new GuessingProfileCreateCommand(
                    normalizedName,
                    GuessRoundProfileSlug.FromName(normalizedName).Value
                )
            );
    }

    public static Validation<
        GuessingProfileDeleteCommand,
        GuessingConfigurationValidationError
    > ValidateDelete(GuessingConfiguration draft)
    {
        var errors = new List<GuessingConfigurationValidationError>();
        ValidateProfileGraph(draft, errors);
        return errors.Count > 0
            ? Validation<
                GuessingProfileDeleteCommand,
                GuessingConfigurationValidationError
            >.Invalid(errors[0], errors.Skip(1).ToArray())
            : Validation<GuessingProfileDeleteCommand, GuessingConfigurationValidationError>.Valid(
                new GuessingProfileDeleteCommand(draft.Profile.Id, draft.Profile.Revision)
            );
    }

    private static GuessRoundProfileSummary? ValidateProfileGraph(
        GuessingConfiguration draft,
        ICollection<GuessingConfigurationValidationError> errors
    )
    {
        if (draft.Profiles.Select(profile => profile.Id).Distinct().Count() != draft.Profiles.Count)
        {
            errors.Add(new GuessingConfigurationValidationError.InvalidProfileSelection());
            return null;
        }

        var selected = draft.Profiles.SingleOrDefault(profile => profile.Id == draft.Profile.Id);
        if (selected is null || selected.Revision != draft.Profile.Revision)
        {
            errors.Add(new GuessingConfigurationValidationError.InvalidProfileSelection());
            return null;
        }

        if (draft.Profiles.Count(profile => profile.IsDefault) != 1)
        {
            errors.Add(new GuessingConfigurationValidationError.InvalidDefaultSelection());
        }

        return selected;
    }

    private static PointAmount? ParseReward(
        string value,
        ICollection<GuessingConfigurationValidationError> errors
    ) =>
        PointAmount
            .ParseNonNegativeAbsolute(value)
            .Match<PointAmount?>(
                amount => amount,
                error =>
                {
                    errors.Add(
                        error == PointAmountParseError.AmountOutOfRange
                            ? new GuessingConfigurationValidationError.RewardOutOfRange()
                            : new GuessingConfigurationValidationError.InvalidReward()
                    );
                    return null;
                }
            );

    private static IReadOnlyList<GuessOptionValue> SnapshotOptions(
        GuessRoundProfileEditor profile,
        ICollection<GuessingConfigurationValidationError> errors
    )
    {
        if (profile.Options.Count == 0)
        {
            errors.Add(new GuessingConfigurationValidationError.NoOptions());
        }

        var target = profile.WhisperAnswerReplies
            ? ReplyDeliveryTarget.Whisper
            : ReplyDeliveryTarget.Chat;
        var values = new List<GuessOptionValue>(profile.Options.Count);
        foreach (var option in profile.Options)
        {
            var names = GuessAnswerNames.Parse(option.Name);
            if (names.IsEmpty)
            {
                errors.Add(new GuessingConfigurationValidationError.OptionNameRequired());
            }
            if (names.Value.Length > _nameMaxLength)
            {
                errors.Add(new GuessingConfigurationValidationError.OptionNameTooLong());
            }

            values.Add(
                new GuessOptionValue(
                    names.Value,
                    string.IsNullOrWhiteSpace(option.ReplyText)
                        ? names.CanonicalDisplayName
                        : option.ReplyText.Trim(),
                    target
                )
            );
        }

        var duplicate = values
            .SelectMany(option => GuessAnswerNames.Parse(option.Name).Values)
            .GroupBy(name => name.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            errors.Add(new GuessingConfigurationValidationError.DuplicateOption(duplicate));
        }

        return values;
    }

    private static string RequiredName(
        string value,
        bool profile,
        ICollection<GuessingConfigurationValidationError> errors
    )
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            errors.Add(
                profile
                    ? new GuessingConfigurationValidationError.ProfileNameRequired()
                    : new GuessingConfigurationValidationError.OptionNameRequired()
            );
        }

        return normalized;
    }

    private static GuessingCommandAliases SnapshotAliases(CommandAliasEditor aliases) =>
        new(
            NormalizeAliases(aliases.StartAliases),
            NormalizeAliases(aliases.StopAliases),
            NormalizeAliases(aliases.WinAliases),
            NormalizeAliases(aliases.GuessAliases),
            NormalizeAliases(aliases.GuessesAliases)
        );

    private static string NormalizeAliases(string aliases) =>
        string.Join(", ", CommandAliasNormalizer.Split(aliases));

    private static GuessingReplySettings SnapshotReplies(ReplySettingsEditor replies) =>
        new(
            replies.RoundStartedReply.Trim(),
            replies.RoundAlreadyOpenReply.Trim(),
            replies.NoOpenRoundReply.Trim(),
            replies.GuessingStoppedReply.Trim(),
            replies.GuessingAlreadyStoppedReply.Trim(),
            replies.GuessingClosedReply.Trim(),
            replies.InvalidGuessReply.Trim(),
            replies.GuessUsageReply.Trim(),
            replies.AvailableGuessesReply.Trim(),
            replies.WinUsageReply.Trim(),
            replies.ModeratorOnlyReply.Trim(),
            replies.WinnerReply.Trim(),
            replies.NoWinnersReply.Trim()
        );
}

public abstract record GuessingConfigurationValidationError
{
    private GuessingConfigurationValidationError() { }

    public abstract string Message { get; }

    public sealed record ProfileNameRequired : GuessingConfigurationValidationError
    {
        public override string Message => "Round type name is required.";
    }

    public sealed record ProfileNameTooLong : GuessingConfigurationValidationError
    {
        public override string Message => "Round type names cannot exceed 128 characters.";
    }

    public sealed record DuplicateProfileName : GuessingConfigurationValidationError
    {
        public override string Message => "A round type with that name already exists.";
    }

    public sealed record InvalidProfileSelection : GuessingConfigurationValidationError
    {
        public override string Message =>
            "That round type changed while you were editing. Reload the page and try again.";
    }

    public sealed record InvalidDefaultSelection : GuessingConfigurationValidationError
    {
        public override string Message => "Choose exactly one default round type.";
    }

    public sealed record InvalidReward : GuessingConfigurationValidationError
    {
        public override string Message => "Point amount must be a whole number.";
    }

    public sealed record RewardOutOfRange : GuessingConfigurationValidationError
    {
        public override string Message => "Point amounts cannot exceed 10^100.";
    }

    public sealed record NoOptions : GuessingConfigurationValidationError
    {
        public override string Message => "Add at least one answer.";
    }

    public sealed record OptionNameRequired : GuessingConfigurationValidationError
    {
        public override string Message => "Answer names cannot be blank.";
    }

    public sealed record OptionNameTooLong : GuessingConfigurationValidationError
    {
        public override string Message =>
            "An answer and its comma-separated aliases cannot exceed 128 characters.";
    }

    public sealed record DuplicateOption(string Name) : GuessingConfigurationValidationError
    {
        public override string Message => $"Answer or alias '{Name}' is entered more than once.";
    }

    public sealed record DuplicateAlias(string Alias) : GuessingConfigurationValidationError
    {
        public override string Message => $"!{Alias} is entered more than once.";
    }

    public sealed record InvalidPinDuration : GuessingConfigurationValidationError
    {
        public override string Message => "Pin duration must be between 30 and 1800 seconds.";
    }
}
