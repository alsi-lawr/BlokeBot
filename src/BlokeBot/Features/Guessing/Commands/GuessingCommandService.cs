using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Text;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Commands;

public sealed class GuessingCommandService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<string> AvailableGuessesReplyAsync(string hostLogin, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured().Message;

        var round = await GuessingRoundQueries.Unresolved(db, hostId.Value).FirstOrDefaultAsync(ct);
        var profileId = round?.GuessRoundProfileId
            ?? await GuessingProfileQueries.DefaultProfileIdAsync(db, hostId.Value, ct);
        var profile = await GuessingProfileQueries.LoadProfileWithSettingsAsync(
            db,
            hostId.Value,
            profileId,
            ct,
            includeOptions: true
        );
        var settings = profile?.ReplySettings
            ?? ReplySettingsMapper.ToEntity(GuessingDefaults.Replies());
        var template = string.IsNullOrWhiteSpace(settings.AvailableGuessesReply)
            ? GuessingDefaults.Replies().AvailableGuessesReply
            : settings.AvailableGuessesReply;

        return TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["round"] = profile?.Name ?? string.Empty,
                ["options"] = FormatOptions(profile?.Options.Select(x => x.Name) ?? []),
            }
        );
    }

    public async Task<string> ModeratorOnlyReplyAsync(string hostLogin, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured().Message;

        var settings = await GuessingProfileQueries.ReplySettingsForRoundOrDefaultAsync(
            db,
            hostId.Value,
            await GuessingRoundQueries.Unresolved(db, hostId.Value).FirstOrDefaultAsync(ct),
            ct
        );
        return settings.ModeratorOnlyReply;
    }

    public async Task<string> UsageReplyAsync(
        string hostLogin,
        GuessCommandKind kind,
        string command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured().Message;

        var settings = await GuessingProfileQueries.ReplySettingsForRoundOrDefaultAsync(
            db,
            hostId.Value,
            await GuessingRoundQueries.Unresolved(db, hostId.Value).FirstOrDefaultAsync(ct),
            ct
        );
        var template = kind switch
        {
            GuessCommandKind.Win => settings.WinUsageReply,
            GuessCommandKind.Start => "Usage: !{command} [round]",
            _ => settings.GuessUsageReply,
        };
        return TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["command"] = command,
            }
        );
    }

    private static GuessingOperationResult NotConfigured() =>
        new(false, "This channel is not configured.");

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

}
