using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutUiTests
{
    [Test]
    public async Task DefaultsAreOffAndNativeWithChatControlsAbsentFromTheDocument()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory);
        await using var context = CreateContext(factory);

        var section = context.Render<AutomaticRaidShoutoutSection>(parameters =>
            parameters.Add(component => component.HostId, hostId)
        );
        Open(section, "Automatic raid shoutouts");

        section.WaitForAssertion(() =>
        {
            section.Find("#automatic-raid-enabled").HasAttribute("checked").ShouldBeFalse();
            section.Find("#automatic-raid-minimum-viewers").GetAttribute("value").ShouldBe("1");
            section.Find("#automatic-raid-minimum-viewers").HasAttribute("disabled").ShouldBeTrue();
            section.Find("#automatic-raid-mechanism-native").HasAttribute("checked").ShouldBeTrue();
            section.FindAll("[data-automatic-raid-chat-controls]").ShouldBeEmpty();
            section.Markup.ShouldContain("Disabled. Saved settings are retained");
        });
    }

    [Test]
    public async Task ChatChoicesRemoveIrrelevantControlsAndRetainEnteredValues()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory);
        var configured = new AutomaticRaidShoutoutConfiguration(
            true,
            12,
            AutomaticRaidShoutoutMechanism.Chat,
            AutomaticRaidChatPresentation.Pinned,
            "Welcome {twitch_handle}: {last_game|something fun}",
            300,
            PersistedAnnouncementColor.Purple
        );
        await SaveAsync(factory, hostId, configured);
        await using var context = CreateContext(factory);

        var section = context.Render<AutomaticRaidShoutoutSection>(parameters =>
            parameters.Add(component => component.HostId, hostId)
        );
        Open(section, "Automatic raid shoutouts");

        section.WaitForAssertion(() =>
        {
            section.Find("#automatic-raid-pin-duration");
            section.FindAll("#automatic-raid-announcement-color").ShouldBeEmpty();
            section.Markup.ShouldContain("{twitch_handle}");
            section.Markup.ShouldContain("{display_name}");
            section.Markup.ShouldContain("{channel_url}");
            section.Markup.ShouldContain("{last_game|fallback}");
            section.Markup.ShouldContain("{stream_title|fallback}");
            section.Markup.ShouldContain("{viewer_count}");
            section.Markup.ShouldContain("150 characters");
            section.Markup.ShouldContain("350 characters");
            section.Markup.ShouldContain("500-character");
        });

        section.Find("#automatic-raid-pin-duration").Input("600");
        section.Find("#automatic-raid-presentation-announcement").Change("Announcement");
        section.FindAll("#automatic-raid-pin-duration").ShouldBeEmpty();
        section
            .Find("#automatic-raid-announcement-color")
            .QuerySelectorAll("option")
            .Length.ShouldBe(5);

        section.Find("#automatic-raid-presentation-pinned").Change("Pinned");
        section.Find("#automatic-raid-pin-duration").GetAttribute("value").ShouldBe("600");
        section.Find("#automatic-raid-mechanism-native").Change("Native");
        section.FindAll("[data-automatic-raid-chat-controls]").ShouldBeEmpty();
        section.Find("#automatic-raid-mechanism-chat").Change("Chat");
        section
            .Find("#automatic-raid-message-template")
            .GetAttribute("value")
            .ShouldBe(configured.MessageTemplate);
    }

    [Test]
    public async Task InvalidTemplateStopsSaveAndOutcomeHistoryUsesTypedTerminalCopy()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory);
        await SaveAsync(
            factory,
            hostId,
            AutomaticRaidShoutoutConfiguration.Defaults with
            {
                Enabled = true,
                Mechanism = AutomaticRaidShoutoutMechanism.Chat,
                ChatPresentation = AutomaticRaidChatPresentation.Pinned,
            }
        );
        await SeedOutcomesAsync(factory, hostId);
        await using var context = CreateContext(factory);

        var section = context.Render<AutomaticRaidShoutoutSection>(parameters =>
            parameters.Add(component => component.HostId, hostId)
        );
        Open(section, "Automatic raid shoutouts");
        section
            .WaitForElement("#automatic-raid-message-template")
            .Input(new string('x', AutomaticRaidShoutoutTemplate.MaximumAuthoredCharacters + 1));
        section.Markup.ShouldContain("150 characters or fewer");

        section.Find("button.btn-primary").Click();
        section.WaitForAssertion(() =>
        {
            section.Find("[data-automatic-raid-validation]");
            section.Markup.ShouldContain("Settings were not saved.");
        });

        Open(section, "Automatic shoutout outcomes");
        section.WaitForAssertion(() =>
        {
            section.FindAll("[data-automatic-raid-outcomes] article").Count.ShouldBe(20);
            section.Markup.ShouldContain("Message sent, pin failed");
            section.Markup.ShouldContain("will not resend or switch modes");
            section.Markup.ShouldContain("Native shoutout cooldown active");
            section.FindAll("[data-automatic-raid-outcomes] button").ShouldBeEmpty();
        });

        await using var db = await factory.CreateDbContextAsync();
        (
            await db.AutomaticRaidShoutoutSettings.SingleAsync(value => value.HostId == hostId)
        ).MessageTemplate.ShouldBe(AutomaticRaidShoutoutDefaults.MessageTemplate);
    }

    private static BunitContext CreateContext(SqliteBlokeBotDbFactory factory)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(
            new AutomaticRaidShoutoutConfigurationService(factory, TimeProvider.System)
        );
        return context;
    }

    private static void Open(IRenderedComponent<AutomaticRaidShoutoutSection> section, string title)
    {
        section
            .FindAll("button.disclosure-trigger")
            .Single(button => button.TextContent.Contains(title, StringComparison.Ordinal))
            .Click();
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SaveAsync(
        SqliteBlokeBotDbFactory factory,
        int hostId,
        AutomaticRaidShoutoutConfiguration configuration
    )
    {
        var service = new AutomaticRaidShoutoutConfigurationService(factory, TimeProvider.System);
        (
            await service.SaveAsync(hostId, configuration, CancellationToken.None)
        ).ShouldBeOfType<AutomaticRaidShoutoutSaveOutcome.Saved>();
    }

    private static async Task SeedOutcomesAsync(SqliteBlokeBotDbFactory factory, int hostId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        for (var index = 0; index < 22; index++)
        {
            var result = index switch
            {
                0 => AutomaticRaidShoutoutResultCode.PartialFailure,
                1 => AutomaticRaidShoutoutResultCode.Cooldown,
                _ => AutomaticRaidShoutoutResultCode.Delivered,
            };
            db.AutomaticRaidShoutoutOutcomes.Add(
                new AutomaticRaidShoutoutOutcome
                {
                    HostId = hostId,
                    ProviderMessageId = $"raid-{index:D2}",
                    SourceTwitchUserId = $"raider-{index}-id",
                    SourceLogin = $"raider{index}",
                    SourceDisplayName = $"Raider {index}",
                    ViewerCount = 20 + index,
                    Status =
                        result == AutomaticRaidShoutoutResultCode.Delivered
                            ? AutomaticRaidShoutoutOutcomeStatus.Delivered
                            : AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                    ResultCode = result,
                    MessageTimestampUtc = now.AddMinutes(-index),
                    ClaimedAtUtc = now.AddMinutes(-index),
                    CompletedAtUtc = now.AddMinutes(-index).AddSeconds(1),
                }
            );
        }
        await db.SaveChangesAsync();
    }
}
