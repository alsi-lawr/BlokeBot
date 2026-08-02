using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
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
                    ["options"] = FormatOptions(profile?.OptionNames ?? []),
                }
            )
        );
    }

    public async Task<string> AvailableGuessesReplyAsync(
        string hostLogin,
        AppCommandRouteState route,
        CancellationToken ct
    ) => (await AvailableGuessesResponseAsync(hostLogin, route, ct)).Message;

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

    public async Task<string> ModeratorOnlyReplyAsync(
        string hostLogin,
        AppCommandRouteState route,
        CancellationToken ct
    ) => (await ModeratorOnlyResponseAsync(hostLogin, route, ct)).Message;

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
        AppCommandRouteState route,
        CancellationToken ct
    ) => (await UsageResponseAsync(hostLogin, kind, command, route, ct)).Message;

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

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static ValueTask<Option<int>> ResolveHostIdAsync(
        BlokeBotDbContext db,
        string hostLogin,
        CancellationToken ct
    ) => BotHostQueries.FindHostId(db, hostLogin).RunAsync(ct);
}
