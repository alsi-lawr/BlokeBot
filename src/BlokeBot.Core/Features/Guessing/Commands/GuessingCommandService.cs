using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Commands;

public sealed class GuessingCommandService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<CommandResponse> AvailableGuessesResponseAsync(
        string hostLogin,
        AppCommandRouteState route,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = (await ResolveHostIdAsync(db, hostLogin, ct)).Match<int?>(
            value => value,
            () => null
        );
        if (hostId is null)
        {
            return NotConfiguredResponse();
        }

        var round = await GuessingRoundQueries.LoadUnresolvedAsync(db, hostId.Value, ct);
        var resolution = await LoadReplySettingsAsync(db, hostId.Value, round, route, ct);

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
                    ["options"] = GuessAnswerNames.FormatOptionList(profile?.OptionNames ?? []),
                }
            )
        );
    }

    public async Task<CommandResponse> ModeratorOnlyResponseAsync(
        string hostLogin,
        AppCommandRouteState route,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = (await ResolveHostIdAsync(db, hostLogin, ct)).Match<int?>(
            value => value,
            () => null
        );
        if (hostId is null)
        {
            return NotConfiguredResponse();
        }

        var round = await GuessingRoundQueries.LoadUnresolvedAsync(db, hostId.Value, ct);
        var resolution = await LoadReplySettingsAsync(db, hostId.Value, round, route, ct);

        return new CommandResponse(
            resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.ModeratorOnly),
            resolution.Settings.ModeratorOnlyReply
        );
    }

    public async Task<CommandResponse> UsageResponseAsync(
        string hostLogin,
        GuessCommandKind kind,
        string command,
        AppCommandRouteState route,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = (await ResolveHostIdAsync(db, hostLogin, ct)).Match<int?>(
            value => value,
            () => null
        );
        if (hostId is null)
        {
            return NotConfiguredResponse();
        }

        var round = await GuessingRoundQueries.LoadUnresolvedAsync(db, hostId.Value, ct);
        var resolution = await LoadReplySettingsAsync(db, hostId.Value, round, route, ct);

        var template = kind switch
        {
            GuessCommandKind.Win => resolution.Settings.WinUsageReply,
            GuessCommandKind.Start => "Usage: !{command} [round]",
            _ => resolution.Settings.GuessUsageReply,
        };
        var target = kind switch
        {
            GuessCommandKind.Win => resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.WinUsage),
            GuessCommandKind.Start => CommandResponseTarget.Chat,
            _ => resolution.ReplyDelivery.TargetFor(GuessingReplyKeys.GuessUsage),
        };
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

    private static Task<GuessingReplySettingsResolution> LoadReplySettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessRoundReference? round,
        AppCommandRouteState route,
        CancellationToken ct
    ) =>
        round is not null
            ? GuessingReplySettingsQueries.LoadForRoundAsync(db, hostId, round.ProfileId, ct)
            : route.Match(
                _ => GuessingReplySettingsQueries.LoadForDefaultAsync(db, hostId, ct),
                guessingProfile =>
                    GuessingReplySettingsQueries.LoadForProfileAsync(
                        db,
                        hostId,
                        guessingProfile.ProfileId,
                        ct
                    )
            );

    private static CommandResponse NotConfiguredResponse() =>
        CommandResponse.Chat("This channel is not set up.");

    private static ValueTask<Option<int>> ResolveHostIdAsync(
        BlokeBotDbContext db,
        string hostLogin,
        CancellationToken ct
    ) => BotHostQueries.FindHostId(db, hostLogin).RunAsync(ct);
}
