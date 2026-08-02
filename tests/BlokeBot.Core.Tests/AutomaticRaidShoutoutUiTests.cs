using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
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

        var section = Render(context, hostId);
        Open(section, "Automatic raid shoutouts");

        section.WaitForAssertion(() =>
        {
            section.Find("#automatic-raid-enabled").HasAttribute("checked").ShouldBeFalse();
            section.Find("#automatic-raid-minimum-viewers").GetAttribute("value").ShouldBe("1");
            section.Find("#automatic-raid-minimum-viewers").HasAttribute("disabled").ShouldBeTrue();
            section.Find("#automatic-raid-mechanism-native").HasAttribute("checked").ShouldBeTrue();
            section.FindAll("[data-automatic-raid-chat-controls]").ShouldBeEmpty();
            section.Markup.ShouldContain("Off. Your settings are saved");
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

        var section = Render(context, hostId);
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
        var colorOptions = section
            .Find("#automatic-raid-announcement-color")
            .QuerySelectorAll("option")
            .ToArray();
        colorOptions
            .Select(option => option.TextContent)
            .ShouldBe(["Default", "Blue", "Green", "Orange", "Purple"]);
        colorOptions
            .Select(option => option.GetAttribute("value"))
            .ShouldBe(["Primary", "Blue", "Green", "Orange", "Purple"]);

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

        var section = Render(context, hostId);
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
            section.Markup.ShouldContain("Skipped during Twitch cooldown");
            section.FindAll("[data-automatic-raid-outcomes] button").ShouldBeEmpty();
        });

        await using var db = await factory.CreateDbContextAsync();
        (
            await db.AutomaticRaidShoutoutSettings.SingleAsync(value => value.HostId == hostId)
        ).MessageTemplate.ShouldBe(AutomaticRaidShoutoutDefaults.MessageTemplate);
    }

    [Test]
    public async Task HostChangeResetsDraftAndGuardedSaveCannotCrossHostIdentity()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostA = await SeedHostAsync(factory, "streamer-a");
        var hostB = await SeedHostAsync(factory, "streamer-b");
        await SaveAsync(
            factory,
            hostA,
            AutomaticRaidShoutoutConfiguration.Defaults with
            {
                Enabled = true,
                MinimumViewerCount = 10,
            }
        );
        await SaveAsync(
            factory,
            hostB,
            AutomaticRaidShoutoutConfiguration.Defaults with
            {
                Enabled = true,
                MinimumViewerCount = 20,
            }
        );
        await using var context = CreateContext(factory);
        var guardEntered = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseGuard = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        async Task GuardAsync(int hostId, Func<Task> mutation)
        {
            guardEntered.SetResult(hostId);
            await releaseGuard.Task;
            await mutation();
        }

        var section = Render(context, hostA, GuardAsync);
        Open(section, "Automatic raid shoutouts");
        section.WaitForElement("#automatic-raid-minimum-viewers").Input("77");

        var save = section.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        (await guardEntered.Task).ShouldBe(hostA);

        section.Render(parameters =>
        {
            parameters.Add(component => component.HostId, hostB);
            parameters.Add(component => component.RunHostMutationAsync, GuardAsync);
        });
        section.WaitForAssertion(() =>
            section.Find("#automatic-raid-minimum-viewers").GetAttribute("value").ShouldBe("20")
        );

        releaseGuard.SetResult();
        await save;

        await using var db = await factory.CreateDbContextAsync();
        (
            await db.AutomaticRaidShoutoutSettings.SingleAsync(value => value.HostId == hostA)
        ).MinimumViewerCount.ShouldBe(77);
        (
            await db.AutomaticRaidShoutoutSettings.SingleAsync(value => value.HostId == hostB)
        ).MinimumViewerCount.ShouldBe(20);
        section.Find("#automatic-raid-minimum-viewers").GetAttribute("value").ShouldBe("20");
        section.Markup.ShouldNotContain("Automatic raid shoutouts saved and enabled.");
    }

    [Test]
    public async Task AlertsChangedRefreshesAutomaticTerminalOutcomes()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory);
        await using var context = CreateContext(factory);
        var section = Render(context, hostId);
        Open(section, "Automatic raid shoutouts");
        Open(section, "Automatic shoutout outcomes");
        section.WaitForAssertion(() =>
            section.Markup.ShouldContain("No automatic raid shoutouts recorded yet.")
        );

        await SeedOutcomesAsync(factory, hostId, count: 1);
        await context
            .Services.GetRequiredService<EventBus<AppEventKind>>()
            .PublishAsync(AppEventKind.AlertsChanged, CancellationToken.None);

        section.WaitForAssertion(() =>
        {
            section.Markup.ShouldContain("Message sent, pin failed");
            section.Markup.ShouldContain("will not resend or switch modes");
        });
    }

    private static BunitContext CreateContext(SqliteBlokeBotDbFactory factory)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(TestEventBus.Create<AppEventKind>());
        context.Services.AddSingleton(
            new AutomaticRaidShoutoutConfigurationService(factory, TimeProvider.System)
        );
        return context;
    }

    private static IRenderedComponent<AutomaticRaidShoutoutSection> Render(
        BunitContext context,
        int hostId,
        Func<int, Func<Task>, Task>? guard = null
    )
    {
        guard ??= static (_, mutation) => mutation();
        return context.Render<AutomaticRaidShoutoutSection>(parameters =>
        {
            parameters.Add(component => component.HostId, hostId);
            parameters.Add(component => component.RunHostMutationAsync, guard);
        });
    }

    private static void Open(
        IRenderedComponent<AutomaticRaidShoutoutSection> section,
        string title
    ) =>
        section
            .FindAll("button.disclosure-trigger")
            .Single(button => button.TextContent.Contains(title, StringComparison.Ordinal))
            .Click();

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory factory,
        string login = "streamer"
    )
    {
        await using var db = await factory.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
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

    private static async Task SeedOutcomesAsync(
        SqliteBlokeBotDbFactory factory,
        int hostId,
        int count = 22
    )
    {
        await using var db = await factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        for (var index = 0; index < count; index++)
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
