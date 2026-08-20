using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public sealed class GuessingConfigurationTransferAdapter
{
    internal static async Task<IReadOnlyList<ConfigurationValidationIssue>> StageAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessingSectionV1 section,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .Profiles.AsSplitQuery()
            .Include(x => x.ReplySettings)
            .Include(x => x.CommandAliases)
            .Include(x => x.Options)
            .Include(x => x.Rounds)
            .Where(x => x.HostId == hostId)
            .ToListAsync(cancellationToken);
        var resolutions = selection.ItemResolutions.ToDictionary(
            x => x.ImportedId,
            StringComparer.Ordinal
        );
        var matches = new Dictionary<string, GuessRoundProfile>(StringComparer.Ordinal);
        foreach (var imported in section.Profiles)
        {
            var resolution = resolutions.GetValueOrDefault(imported.Id);
            var match = resolution?.TargetId is { } targetId
                ? existing.SingleOrDefault(x => x.Id == targetId)
                : existing.SingleOrDefault(x =>
                    string.Equals(x.Slug, imported.Slug, StringComparison.Ordinal)
                );
            if (match is not null)
            {
                matches[imported.Id] = match;
            }
        }

        var retained = resolutions
            .Values.Where(x =>
                x.Resolution == ImportConflictResolution.Retain && x.TargetId is not null
            )
            .Select(x => x.TargetId!.Value)
            .ToHashSet();
        if (selection.Strategy == ImportConflictStrategy.ReplaceSection)
        {
            var matchedIds = matches.Values.Select(x => x.Id).ToHashSet();
            var blocked = existing.FirstOrDefault(x =>
                !matchedIds.Contains(x.Id) && x.Rounds.Count > 0 && !retained.Contains(x.Id)
            );
            if (blocked is not null)
            {
                return
                [
                    new(
                        $"sections.guessing.profiles[{blocked.Slug}]",
                        $"Profile '{blocked.Name}' has retained round history. Retain it or abort the import."
                    ),
                ];
            }
        }

        var finalSummaries = BuildFinalSummaries(existing, section, selection, matches, retained);
        var validated =
            new List<(GuessingProfileV1 Imported, GuessingConfigurationSaveCommand Command)>();
        foreach (var imported in section.Profiles)
        {
            if (
                selection.Strategy == ImportConflictStrategy.AddMissing
                && matches.ContainsKey(imported.Id)
            )
            {
                continue;
            }
            var target = matches.GetValueOrDefault(imported.Id);
            var editor = MapEditor(
                imported,
                target?.Id ?? TemporaryId(imported.Id),
                target?.Revision ?? 0
            );
            var draft = new GuessingConfiguration(
                MapAliases(imported.CommandAliases),
                new ReplyDeliveryEditor(),
                new GuessingPinEditor(),
                false,
                finalSummaries,
                editor
            );
            var validation = GuessingConfigurationValidator
                .Validate(draft)
                .Match<ValidationResult>(
                    command => new ValidationResult.Valid(command),
                    errors => new ValidationResult.Invalid(errors.Select(x => x.Message).ToArray())
                );
            if (validation is ValidationResult.Invalid invalid)
            {
                return invalid
                    .Errors.Select(message => new ConfigurationValidationIssue(
                        $"sections.guessing.profiles[{imported.Id}]",
                        message
                    ))
                    .ToArray();
            }
            validated.Add((imported, ((ValidationResult.Valid)validation).Command));
        }

        foreach (var target in matches.Values.Distinct())
        {
            target.Slug = $"import-{Guid.NewGuid():N}";
            target.IsDefault = false;
        }
        _ = await db.SaveChangesAsync(cancellationToken);

        if (selection.Strategy == ImportConflictStrategy.ReplaceSection)
        {
            var matchedIds = matches.Values.Select(x => x.Id).Concat(retained).ToHashSet();
            db.Profiles.RemoveRange(
                existing.Where(x => !matchedIds.Contains(x.Id) && x.Rounds.Count == 0)
            );
        }
        foreach (var (imported, command) in validated)
        {
            var profile =
                matches.GetValueOrDefault(imported.Id) ?? new GuessRoundProfile { HostId = hostId };
            if (profile.Id == 0)
            {
                _ = db.Profiles.Add(profile);
            }

            var aliasFailure = await GuessingConfigurationGraphStager.FindAliasFailureAsync(
                db,
                hostId,
                command,
                cancellationToken
            );
            if (aliasFailure is not null)
            {
                return
                [
                    new(
                        $"sections.guessing.profiles[{imported.Id}].commandAliases",
                        aliasFailure.Message
                    ),
                ];
            }
            profile.Revision++;
            GuessingConfigurationGraphStager.ApplyProfile(db, hostId, profile, command);
        }
        return [];
    }

    private static IReadOnlyList<GuessRoundProfileSummary> BuildFinalSummaries(
        IReadOnlyList<GuessRoundProfile> existing,
        GuessingSectionV1 section,
        SectionImportSelection selection,
        IReadOnlyDictionary<string, GuessRoundProfile> matches,
        IReadOnlySet<int> retained
    )
    {
        var summaries =
            selection.Strategy == ImportConflictStrategy.ReplaceSection
                ? existing.Where(x => retained.Contains(x.Id)).Select(ToSummary).ToList()
                : existing.Select(ToSummary).ToList();
        foreach (var imported in section.Profiles)
        {
            var target = matches.GetValueOrDefault(imported.Id);
            if (selection.Strategy == ImportConflictStrategy.AddMissing && target is not null)
            {
                continue;
            }

            if (target is not null)
            {
                _ = summaries.RemoveAll(x => x.Id == target.Id);
            }

            summaries.Add(
                new(
                    target?.Id ?? TemporaryId(imported.Id),
                    target?.Revision ?? 0,
                    imported.Name,
                    imported.IsDefault
                )
            );
        }
        return summaries;
    }

    private static GuessRoundProfileEditor MapEditor(
        GuessingProfileV1 value,
        int id,
        long revision
    ) =>
        new()
        {
            Id = id,
            Revision = revision,
            Name = value.Name,
            IsDefault = value.IsDefault,
            WinningGuessPointReward = value.WinningGuessPointReward,
            WhisperAnswerReplies =
                value.Options.Count > 0
                && value.Options.All(x => x.ReplyTarget == ReplyDeliveryTarget.Whisper),
            Replies = MapReplies(value.Replies),
            Options = value
                .Options.Select(x => new GuessOptionEditor
                {
                    Name = x.Name,
                    ReplyText = x.ReplyText,
                    ReplyTarget = x.ReplyTarget,
                })
                .ToList(),
        };

    private static CommandAliasEditor MapAliases(IReadOnlyList<CommandAliasesV1> values)
    {
        var aliases = values.ToDictionary(x => x.Command, x => string.Join(", ", x.Aliases));
        return new()
        {
            StartAliases = aliases.GetValueOrDefault(AppCommandKind.Start) ?? string.Empty,
            StopAliases = aliases.GetValueOrDefault(AppCommandKind.Stop) ?? string.Empty,
            WinAliases = aliases.GetValueOrDefault(AppCommandKind.Win) ?? string.Empty,
            GuessAliases = aliases.GetValueOrDefault(AppCommandKind.Guess) ?? string.Empty,
            GuessesAliases = aliases.GetValueOrDefault(AppCommandKind.Guesses) ?? string.Empty,
        };
    }

    private static ReplySettingsEditor MapReplies(GuessingRepliesV1 x) =>
        new()
        {
            RoundStartedReply = x.RoundStarted,
            RoundAlreadyOpenReply = x.RoundAlreadyOpen,
            NoOpenRoundReply = x.NoOpenRound,
            GuessingStoppedReply = x.GuessingStopped,
            GuessingAlreadyStoppedReply = x.GuessingAlreadyStopped,
            GuessingClosedReply = x.GuessingClosed,
            InvalidGuessReply = x.InvalidGuess,
            GuessUsageReply = x.GuessUsage,
            AvailableGuessesReply = x.AvailableGuesses,
            WinUsageReply = x.WinUsage,
            ModeratorOnlyReply = x.ModeratorOnly,
            WinnerReply = x.Winner,
            NoWinnersReply = x.NoWinners,
        };

    private static GuessRoundProfileSummary ToSummary(GuessRoundProfile x) =>
        new(x.Id, x.Revision, x.Name, x.IsDefault);

    private static string CanonicalSlug(string name) => GuessRoundProfileSlug.FromName(name).Value;

    private static int TemporaryId(string value) =>
        -Math.Abs(StringComparer.Ordinal.GetHashCode(value) | 1);

    private abstract record ValidationResult
    {
        private ValidationResult() { }

        public sealed record Valid(GuessingConfigurationSaveCommand Command) : ValidationResult;

        public sealed record Invalid(IReadOnlyList<string> Errors) : ValidationResult;
    }
}
