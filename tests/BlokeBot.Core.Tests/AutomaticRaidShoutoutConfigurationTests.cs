using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutConfigurationTests
{
    [Test]
    public async Task MissingRows_LoadAcceptedDefaultsPerExistingHost()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory, "first");
        var service = new AutomaticRaidShoutoutConfigurationService(factory, TimeProvider.System);

        var configuration = await service.LoadAsync(hostId, CancellationToken.None);

        configuration.ShouldBe(AutomaticRaidShoutoutConfiguration.Defaults);
        configuration!.Enabled.ShouldBeFalse();
        configuration.MinimumViewerCount.ShouldBe(1);
        configuration.Mechanism.ShouldBe(AutomaticRaidShoutoutMechanism.Native);
        configuration.ChatPresentation.ShouldBe(AutomaticRaidChatPresentation.Regular);
        configuration.AnnouncementColor.ShouldBe(PersistedAnnouncementColor.Primary);
    }

    [Test]
    public async Task SaveLoadDisableAndPreview_AreHostIsolatedAndRetainChatConfiguration()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstId = await SeedHostAsync(factory, "first");
        var secondId = await SeedHostAsync(factory, "second");
        var service = new AutomaticRaidShoutoutConfigurationService(factory, TimeProvider.System);
        var configured = new AutomaticRaidShoutoutConfiguration(
            true,
            12,
            AutomaticRaidShoutoutMechanism.Chat,
            AutomaticRaidChatPresentation.Pinned,
            "Raid from {display_name}: {last_game|something fun}",
            null,
            PersistedAnnouncementColor.Purple
        );

        (
            await service.SaveAsync(firstId, configured, CancellationToken.None)
        ).ShouldBeOfType<AutomaticRaidShoutoutSaveOutcome.Saved>();
        var disabled = configured with { Enabled = false };
        (
            await service.SaveAsync(firstId, disabled, CancellationToken.None)
        ).ShouldBeOfType<AutomaticRaidShoutoutSaveOutcome.Saved>();

        (await service.LoadAsync(firstId, CancellationToken.None)).ShouldBe(disabled);
        (await service.LoadAsync(secondId, CancellationToken.None)).ShouldBe(
            AutomaticRaidShoutoutConfiguration.Defaults
        );
        var preview = await service.PreviewAsync(
            firstId,
            new("@raider", "Raider", "https://twitch.tv/raider", 12, null, null),
            CancellationToken.None
        );
        preview
            .ShouldBeOfType<AutomaticRaidShoutoutPreviewOutcome.Rendered>()
            .Message.ShouldBe("Raid from Raider: something fun");
    }

    [Test]
    public async Task Save_RejectsEnumsThresholdPinAndTemplateWithoutPersisting()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory, "first");
        var service = new AutomaticRaidShoutoutConfigurationService(factory, TimeProvider.System);
        var invalid = new AutomaticRaidShoutoutConfiguration(
            true,
            0,
            (AutomaticRaidShoutoutMechanism)99,
            AutomaticRaidChatPresentation.Pinned,
            "{bad}",
            29,
            (PersistedAnnouncementColor)99
        );

        var rejected = (
            await service.SaveAsync(hostId, invalid, CancellationToken.None)
        ).ShouldBeOfType<AutomaticRaidShoutoutSaveOutcome.Invalid>();

        rejected
            .Errors.Select(error => error.Field)
            .ShouldBe(
                [
                    AutomaticRaidShoutoutValidationField.MinimumViewerCount,
                    AutomaticRaidShoutoutValidationField.Mechanism,
                    AutomaticRaidShoutoutValidationField.AnnouncementColor,
                    AutomaticRaidShoutoutValidationField.PinDuration,
                    AutomaticRaidShoutoutValidationField.MessageTemplate,
                ],
                ignoreOrder: true
            );
        await using var db = await factory.CreateDbContextAsync();
        (await db.AutomaticRaidShoutoutSettings.CountAsync()).ShouldBe(0);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory factory, string login)
    {
        await using var db = await factory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
