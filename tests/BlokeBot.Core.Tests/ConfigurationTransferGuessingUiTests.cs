using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.ConfigurationTransfer.Page;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferGuessingUiTests
{
    [Test]
    public async Task OperatorSelectedProfileMapping_UpdatesChosenHistoryBoundTargetInPlace()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(database);
        await using var context = UiTestContextFactory.Create(database, seeded.HostId);
        _ = context.Services.AddBlokeBotConfigurationTransfer();
        var document = Document();
        var json = System.Text.Encoding.UTF8.GetString(
            context.Services.GetRequiredService<ConfigurationDocumentCodec>().Serialize(document)
        );
        context
            .Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("configuration-transfer#import");
        var page = context.Render<ConfigurationTransferPage>();

        page.Find("#configuration-transfer-json").Change(json);
        page.Find("#configuration-transfer-preview").Click();
        var mapping = page.WaitForElement("#guessing-profile-map-imported-profile");

        mapping.Change(
            seeded.ChosenProfileId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        page.WaitForAssertion(() =>
            page.Find("#configuration-transfer-apply").HasAttribute("disabled").ShouldBeFalse()
        );
        page.Find("#configuration-transfer-apply").Click();

        await using var final = await database.CreateDbContextAsync();
        var profiles = await final.Profiles.OrderBy(x => x.Id).ToArrayAsync();
        profiles.ShouldHaveSingleItem().Id.ShouldBe(seeded.ChosenProfileId);
        var stored = await final.Profiles.SingleAsync();
        stored.Id.ShouldBe(seeded.ChosenProfileId);
        stored.Name.ShouldBe("Imported profile");
        stored.Slug.ShouldBe("imported-profile");
        (await final.Rounds.SingleAsync()).GuessRoundProfileId.ShouldBe(seeded.ChosenProfileId);
        (await final.Profiles.AnyAsync(x => x.Id == seeded.AutomaticProfileId)).ShouldBeFalse();
    }

    private static async Task<SeededProfiles> SeedAsync(SqliteBlokeBotDbFactory database)
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.Guessing,
            CreatedAtUtc = now,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var automatic = Profile(host.Id, "Imported profile", "imported-profile", true);
        var chosen = Profile(host.Id, "Destination history", "destination-history", false);
        chosen.Rounds.Add(
            new()
            {
                HostId = host.Id,
                Status = GuessRoundStatus.Closed,
                StartedAtUtc = now.AddHours(-1),
                ClosedAtUtc = now,
            }
        );
        db.Profiles.AddRange(automatic, chosen);
        _ = await db.SaveChangesAsync();
        return new(host.Id, automatic.Id, chosen.Id);
    }

    private static GuessRoundProfile Profile(
        int hostId,
        string name,
        string slug,
        bool isDefault
    ) =>
        new()
        {
            HostId = hostId,
            Name = name,
            Slug = slug,
            IsDefault = isDefault,
            ReplySettings = new(),
            Options = [new() { Name = "answer", ReplyText = "answer" }],
        };

    private static ConfigurationDocumentV1 Document() =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            new("source", "0.12.0"),
            new(
                Guessing: new([
                    new(
                        "imported-profile",
                        "Imported profile",
                        "imported-profile",
                        true,
                        "0",
                        [],
                        new("", "", "", "", "", "", "", "", "", "", "", "", ""),
                        [new("answer", "answer", ReplyDeliveryTarget.Chat)]
                    ),
                ])
            )
        );

    private sealed record SeededProfiles(int HostId, int AutomaticProfileId, int ChosenProfileId);
}
