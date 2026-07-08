using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Text;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Rounds;

public sealed class GuessingRoundService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingChangeNotifier changes
)
{
    public async Task<GuessingOperationResult> DeclareWinnerAsync(
        int hostId,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var round = await GuessingRoundQueries.Unresolved(db, hostId).FirstOrDefaultAsync(ct);
        var settings = await GuessingProfileQueries.ReplySettingsForRoundOrDefaultAsync(
            db,
            hostId,
            round,
            ct
        );
        var normalizedName = GuessName.Parse(name).Value;

        if (round is null)
            return new GuessingOperationResult(false, settings.NoOpenRoundReply);

        var optionExists = await db.GuessOptions.AnyAsync(
            x => x.GuessRoundProfileId == round.GuessRoundProfileId && x.Name == normalizedName,
            ct
        );
        if (!optionExists)
        {
            return new GuessingOperationResult(
                false,
                Format(settings.InvalidGuessReply, normalizedName, string.Empty)
            );
        }

        var winners = await db
            .Votes.AsNoTracking()
            .Where(x => x.GuessRoundId == round.Id && x.GuessName == normalizedName)
            .OrderBy(x => x.GuessedAtUtc)
            .Select(x => x.Login)
            .ToListAsync(ct);

        round.Status = GuessRoundStatus.Completed;
        round.ClosedAtUtc ??= DateTime.UtcNow;
        round.WinningName = normalizedName;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();

        var template = winners.Count == 0 ? settings.NoWinnersReply : settings.WinnerReply;
        var message = TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = normalizedName,
                ["winners"] = winners.Count == 0 ? "none" : string.Join(", ", winners),
                ["count"] = winners.Count.ToString(),
            }
        );
        return new GuessingOperationResult(true, message);
    }

    public async Task<GuessingOperationResult> DeclareWinnerAsync(
        string hostLogin,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        return hostId is null ? NotConfigured() : await DeclareWinnerAsync(hostId.Value, name, ct);
    }

    public async Task<GuessingOperationResult> StartRoundAsync(
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var profile = await GuessingProfileQueries.LoadProfileWithSettingsAsync(
            db,
            hostId,
            profileId,
            ct,
            includeOptions: true
        );
        if (profile is null)
            return new GuessingOperationResult(false, "Round profile not found.");

        var settings = profile.ReplySettings!;
        if (await GuessingRoundQueries.Unresolved(db, hostId).AnyAsync(ct))
            return new GuessingOperationResult(false, settings.RoundAlreadyOpenReply);

        db.Rounds.Add(
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return new GuessingOperationResult(
            true,
            FormatRoundStarted(
                settings.RoundStartedReply,
                profile.Name,
                FormatOptions(profile.Options.Select(x => x.Name))
            )
        );
    }

    public async Task<GuessingOperationResult> StartRoundAsync(
        string hostLogin,
        string? profileName,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured();

        var profile =
            string.IsNullOrWhiteSpace(profileName)
                ? await GuessingProfileQueries.DefaultProfileWithSettingsAsync(db, hostId.Value, ct)
                : await GuessingProfileQueries.LoadProfileByNameAsync(
                    db,
                    hostId.Value,
                    profileName,
                    ct
                );

        if (profile is null)
            return new GuessingOperationResult(false, $"Unknown round profile: {profileName}.");

        return await StartRoundAsync(hostId.Value, profile.Id, ct);
    }

    public async Task<GuessingOperationResult> StopGuessingAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var round = await GuessingRoundQueries.Open(db, hostId).FirstOrDefaultAsync(ct);
        var settings = await GuessingProfileQueries.ReplySettingsForRoundOrDefaultAsync(
            db,
            hostId,
            round ?? await GuessingRoundQueries.Unresolved(db, hostId).FirstOrDefaultAsync(ct),
            ct
        );

        if (round is null)
            return new GuessingOperationResult(false, settings.NoOpenRoundReply);

        round.Status = GuessRoundStatus.Closed;
        round.ClosedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return new GuessingOperationResult(true, settings.GuessingStoppedReply);
    }

    public async Task<GuessingOperationResult> StopGuessingAsync(
        string hostLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        return hostId is null ? NotConfigured() : await StopGuessingAsync(hostId.Value, ct);
    }

    private static string Format(string template, string name, string login) =>
        TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["login"] = login,
            }
        );

    private static string FormatRoundStarted(string template, string round, string options) =>
        TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["round"] = round,
                ["options"] = options,
            }
        );

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static GuessingOperationResult NotConfigured() =>
        new(false, "This channel is not configured.");
}
