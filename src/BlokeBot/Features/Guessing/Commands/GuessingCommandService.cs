using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.Replies;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Commands;

public sealed class GuessingCommandService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<CommandResponse> AvailableGuessesResponseAsync(
        string hostLogin,
        int? profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
        {
            return NotConfiguredResponse();
        }

        var round = await GuessingRoundQueries.LoadUnresolvedAsync(db, hostId.Value, ct);
        GuessingReplySettingsResolution resolution;
        if (round is not null)
        {
            resolution = await GuessingReplySettingsQueries.LoadForRoundAsync(
                db,
                hostId.Value,
                round.ProfileId,
                ct
            );
        }
        else if (profileId is { } requestedProfileId)
        {
            resolution = await GuessingReplySettingsQueries.LoadForProfileAsync(
                db,
                hostId.Value,
                requestedProfileId,
                ct
            );
        }
        else
        {
            resolution = await GuessingReplySettingsQueries.LoadForDefaultAsync(
                db,
                hostId.Value,
                ct
            );
        }

        var profile = await db.Profiles.LoadProfileWithOptionsAsync(
            hostId.Value,
            resolution.ProfileId,
            ct
        );
        var settings = resolution.Settings;
        var template = string.IsNullOrWhiteSpace(settings.AvailableGuessesReply)
            ? GuessingDefaults.Replies().AvailableGuessesReply
            : settings.AvailableGuessesReply;

        return new CommandResponse(
            resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.AvailableGuesses),
            MessageTemplateFormatter.Format(
                template,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["round"] = profile?.Name ?? string.Empty,
                    ["options"] = FormatOptions(profile?.OptionNames ?? []),
                }
            )
        );
    }

    public async Task<string> AvailableGuessesReplyAsync(
        string hostLogin,
        int? profileId,
        CancellationToken ct
    )
    {
        return (await AvailableGuessesResponseAsync(hostLogin, profileId, ct)).Message;
    }

    public async Task<CommandResponse> ModeratorOnlyResponseAsync(
        string hostLogin,
        int? profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
        {
            return NotConfiguredResponse();
        }

        var round = await GuessingRoundQueries.LoadUnresolvedAsync(db, hostId.Value, ct);
        GuessingReplySettingsResolution resolution;
        if (round is not null)
        {
            resolution = await GuessingReplySettingsQueries.LoadForRoundAsync(
                db,
                hostId.Value,
                round.ProfileId,
                ct
            );
        }
        else if (profileId is { } requestedProfileId)
        {
            resolution = await GuessingReplySettingsQueries.LoadForProfileAsync(
                db,
                hostId.Value,
                requestedProfileId,
                ct
            );
        }
        else
        {
            resolution = await GuessingReplySettingsQueries.LoadForDefaultAsync(
                db,
                hostId.Value,
                ct
            );
        }

        return new CommandResponse(
            resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.ModeratorOnly),
            resolution.Settings.ModeratorOnlyReply
        );
    }

    public async Task<string> ModeratorOnlyReplyAsync(
        string hostLogin,
        int? profileId,
        CancellationToken ct
    )
    {
        return (await ModeratorOnlyResponseAsync(hostLogin, profileId, ct)).Message;
    }

    public async Task<CommandResponse> UsageResponseAsync(
        string hostLogin,
        GuessCommandKind kind,
        string command,
        int? profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
        {
            return NotConfiguredResponse();
        }

        var round = await GuessingRoundQueries.LoadUnresolvedAsync(db, hostId.Value, ct);
        GuessingReplySettingsResolution resolution;
        if (round is not null)
        {
            resolution = await GuessingReplySettingsQueries.LoadForRoundAsync(
                db,
                hostId.Value,
                round.ProfileId,
                ct
            );
        }
        else if (profileId is { } requestedProfileId)
        {
            resolution = await GuessingReplySettingsQueries.LoadForProfileAsync(
                db,
                hostId.Value,
                requestedProfileId,
                ct
            );
        }
        else
        {
            resolution = await GuessingReplySettingsQueries.LoadForDefaultAsync(
                db,
                hostId.Value,
                ct
            );
        }

        var template = kind switch
        {
            GuessCommandKind.Win => resolution.Settings.WinUsageReply,
            GuessCommandKind.Start => "Usage: !{command} [round]",
            _ => resolution.Settings.GuessUsageReply,
        };
        var target =
            kind == GuessCommandKind.Win
                ? resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.WinUsage)
            : kind == GuessCommandKind.Start ? CommandResponseTarget.Chat
            : resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.GuessUsage);
        return new CommandResponse(
            target,
            MessageTemplateFormatter.Format(
                template,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["command"] = command,
                }
            )
        );
    }

    public async Task<string> UsageReplyAsync(
        string hostLogin,
        GuessCommandKind kind,
        string command,
        int? profileId,
        CancellationToken ct
    )
    {
        return (await UsageResponseAsync(hostLogin, kind, command, profileId, ct)).Message;
    }

    private static GuessingOperationResult NotConfigured()
    {
        return new(false, "This channel is not set up.");
    }

    private static CommandResponse NotConfiguredResponse()
    {
        return CommandResponse.Chat(NotConfigured().Message);
    }

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }
}
