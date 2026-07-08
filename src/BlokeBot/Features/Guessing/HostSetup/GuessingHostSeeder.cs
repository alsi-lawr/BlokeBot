using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.HostSetup;

public sealed class GuessingHostSeeder(IDbContextFactory<BlokeBotDbContext> dbFactory)
    : IBotHostSeeder
{
    public async Task SeedAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Hosts.AnyAsync(x => x.Id == hostId, ct))
            return;

        if (!await db.CommandAliases.AnyAsync(x => x.HostId == hostId, ct))
        {
            foreach (var (kind, aliases) in GuessingDefaults.Aliases)
                foreach (var alias in aliases)
                    db.CommandAliases.Add(
                        new CommandAlias
                        {
                            HostId = hostId,
                            Kind = kind.ToString(),
                            Alias = alias,
                        }
                    );
        }

        if (!await db.Profiles.AnyAsync(x => x.HostId == hostId, ct))
        {
            db.Profiles.Add(
                new GuessRoundProfile
                {
                    HostId = hostId,
                    Name = "Default",
                    Slug = "default",
                    IsDefault = true,
                    ReplySettings = ToEntity(GuessingDefaults.Replies()),
                }
            );
        }

        await db.SaveChangesAsync(ct);
    }

    private static BotReplySettings ToEntity(ReplySettingsEditor editor) =>
        new()
        {
            RoundStartedReply = editor.RoundStartedReply,
            RoundAlreadyOpenReply = editor.RoundAlreadyOpenReply,
            NoOpenRoundReply = editor.NoOpenRoundReply,
            GuessingStoppedReply = editor.GuessingStoppedReply,
            GuessingAlreadyStoppedReply = editor.GuessingAlreadyStoppedReply,
            GuessingClosedReply = editor.GuessingClosedReply,
            InvalidGuessReply = editor.InvalidGuessReply,
            GuessUsageReply = editor.GuessUsageReply,
            AvailableGuessesReply = editor.AvailableGuessesReply,
            WinUsageReply = editor.WinUsageReply,
            ModeratorOnlyReply = editor.ModeratorOnlyReply,
            WinnerReply = editor.WinnerReply,
            NoWinnersReply = editor.NoWinnersReply,
        };
}
