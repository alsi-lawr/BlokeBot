using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.Replies;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Commands;

public sealed class GuessingCommandService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<TwitchCommandResponse> AvailableGuessesResponseAsync(
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

        var round = await GuessingRoundQueries.Unresolved(db, hostId.Value).FirstOrDefaultAsync(ct);
        var selectedProfileId =
            round?.GuessRoundProfileId
            ?? profileId
            ?? await GuessingProfileQueries.DefaultProfileIdAsync(db, hostId.Value, ct);
        var profile = await GuessingProfileQueries.LoadProfileWithSettingsAsync(
            db,
            hostId.Value,
            selectedProfileId,
            ct,
            includeOptions: true
        );
        var delivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId.Value,
            ReplyDeliveryFeature.Guessing,
            selectedProfileId,
            ct
        );
        var settings =
            profile?.ReplySettings ?? ReplySettingsMapper.ToEntity(GuessingDefaults.Replies());
        var template = string.IsNullOrWhiteSpace(settings.AvailableGuessesReply)
            ? GuessingDefaults.Replies().AvailableGuessesReply
            : settings.AvailableGuessesReply;

        return new TwitchCommandResponse(
            delivery.TargetFor(GuessingReplyKeys.AvailableGuesses),
            MessageTemplateFormatter.Format(
                template,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["round"] = profile?.Name ?? string.Empty,
                    ["options"] = FormatOptions(profile?.Options.Select(x => x.Name) ?? []),
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

    public async Task<TwitchCommandResponse> ModeratorOnlyResponseAsync(
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

        var resolution =
            await GuessingProfileQueries.ReplySettingsResolutionForRoundOrProfileOrDefaultAsync(
                db,
                hostId.Value,
                await GuessingRoundQueries.Unresolved(db, hostId.Value).FirstOrDefaultAsync(ct),
                profileId,
                ct
            );
        return new TwitchCommandResponse(
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

    public async Task<TwitchCommandResponse> UsageResponseAsync(
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

        var resolution =
            await GuessingProfileQueries.ReplySettingsResolutionForRoundOrProfileOrDefaultAsync(
                db,
                hostId.Value,
                await GuessingRoundQueries.Unresolved(db, hostId.Value).FirstOrDefaultAsync(ct),
                profileId,
                ct
            );
        var template = kind switch
        {
            GuessCommandKind.Win => resolution.Settings.WinUsageReply,
            GuessCommandKind.Start => "Usage: !{command} [round]",
            _ => resolution.Settings.GuessUsageReply,
        };
        var target =
            kind == GuessCommandKind.Win
                ? resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.WinUsage)
            : kind == GuessCommandKind.Start ? TwitchCommandResponseTarget.Chat
            : resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.GuessUsage);
        return new TwitchCommandResponse(
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

    private static TwitchCommandResponse NotConfiguredResponse()
    {
        return TwitchCommandResponse.Chat(NotConfigured().Message);
    }

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }
}
