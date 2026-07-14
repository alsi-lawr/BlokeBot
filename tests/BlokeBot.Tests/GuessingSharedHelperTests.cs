using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class GuessingSharedHelperTests
{
    [Test]
    public void MissingAvailableGuessesReply_MappingToEditor_UsesDefaultReply()
    {
        var editor = ReplySettingsMapper.ToEditor(new BotReplySettings());

        editor.AvailableGuessesReply.ShouldBe(GuessingDefaults.Replies().AvailableGuessesReply);
    }

    [Test]
    public async Task UnresolvedRoundWithNondefaultProfile_QueryingReplySettings_UsesRoundProfile()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();

        db.Profiles.Add(
            new GuessRoundProfile
            {
                HostId = host.Id,
                Name = "Default",
                Slug = "default",
                IsDefault = true,
                ReplySettings = new BotReplySettings { NoOpenRoundReply = "default" },
            }
        );
        var activeProfile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Special",
            Slug = "special",
            ReplySettings = new BotReplySettings { NoOpenRoundReply = "special" },
        };
        db.Profiles.Add(activeProfile);
        await db.SaveChangesAsync();

        var settings = (
            await GuessingProfileQueries.ResolveReplySettingsAsync(
                db,
                host.Id,
                activeProfile.Id,
                null,
                CancellationToken.None
            )
        ).Settings;

        settings.NoOpenRoundReply.ShouldBe("special");
    }
}
