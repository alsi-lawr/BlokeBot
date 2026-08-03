using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostConfig.StartupMessage;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class StartupMessageConfigurationTests
{
    [Test]
    public async Task LegacyAndPerHostOverrides_LoadingEffectiveMessages_UseFallbackInIsolation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedHostAsync(dbFactory, "legacy", null, null);
        _ = await SeedHostAsync(dbFactory, "custom", true, "Custom hello");
        _ = await SeedHostAsync(dbFactory, "quiet", false, "Retained text");
        var service = Service(dbFactory);

        var legacy = await service.GetAsync("legacy", CancellationToken.None);
        var custom = await service.GetAsync("custom", CancellationToken.None);
        var quiet = await service.GetAsync("quiet", CancellationToken.None);

        legacy.ShouldBe(new StartupChatMessage.Enabled("Beep boop."));
        custom.ShouldBe(new StartupChatMessage.Enabled("Custom hello"));
        _ = quiet.ShouldBeOfType<StartupChatMessage.Disabled>();
    }

    [Test]
    [Arguments(AuthRole.Streamer)]
    [Arguments(AuthRole.Admin)]
    public async Task StreamerOrAdministrator_SavingValidMessage_NormalizesAndPersists(
        AuthRole role
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", null, null);
        var service = Service(dbFactory);

        var outcome = await service.SaveAsync(
            Session(hostId, role),
            hostId,
            new StartupMessageSaveCommand(true, "  Welcome everyone!  "),
            CancellationToken.None
        );

        outcome
            .ShouldBeOfType<StartupMessageSaveOutcome.Saved>()
            .Configuration.ShouldBe(new StartupMessageConfiguration(true, "Welcome everyone!"));
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.Hosts.SingleAsync(x => x.Id == hostId);
        stored.StartupMessageEnabled.ShouldBe(true);
        stored.StartupMessageText.ShouldBe("Welcome everyone!");
    }

    [Test]
    public async Task Moderator_SavingMessage_IsUnauthorizedWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", null, null);
        var service = Service(dbFactory);

        var outcome = await service.SaveAsync(
            Session(hostId, AuthRole.Moderator),
            hostId,
            new StartupMessageSaveCommand(false, string.Empty),
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<StartupMessageSaveOutcome.Unauthorized>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.Hosts.SingleAsync(x => x.Id == hostId);
        stored.StartupMessageEnabled.ShouldBeNull();
        stored.StartupMessageText.ShouldBeNull();
    }

    [Test]
    public async Task InvalidEnabledText_Saving_RejectsBeforePersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", null, null);
        var service = Service(dbFactory, maximumLength: 10);
        var session = Session(hostId, AuthRole.Streamer);

        var blank = await service.SaveAsync(
            session,
            hostId,
            new StartupMessageSaveCommand(true, "   "),
            CancellationToken.None
        );
        var tooLong = await service.SaveAsync(
            session,
            hostId,
            new StartupMessageSaveCommand(true, "12345678901"),
            CancellationToken.None
        );

        _ = blank.ShouldBeOfType<StartupMessageSaveOutcome.TextRequired>();
        tooLong.ShouldBeOfType<StartupMessageSaveOutcome.TextTooLong>().MaximumLength.ShouldBe(10);
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.Hosts.SingleAsync(x => x.Id == hostId);
        stored.StartupMessageEnabled.ShouldBeNull();
        stored.StartupMessageText.ShouldBeNull();
    }

    [Test]
    public async Task EnabledOverride_SavingThenDisabling_ChangesNextEffectiveLoad()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", null, null);
        var service = Service(dbFactory);
        var session = Session(hostId, AuthRole.Streamer);
        _ = await service.SaveAsync(
            session,
            hostId,
            new StartupMessageSaveCommand(true, "Hello"),
            CancellationToken.None
        );

        (await service.GetAsync("streamer", CancellationToken.None)).ShouldBe(
            new StartupChatMessage.Enabled("Hello")
        );
        _ = await service.SaveAsync(
            session,
            hostId,
            new StartupMessageSaveCommand(false, "Hello"),
            CancellationToken.None
        );

        _ = (
            await service.GetAsync("streamer", CancellationToken.None)
        ).ShouldBeOfType<StartupChatMessage.Disabled>();
    }

    [Test]
    public async Task RetainedOverLimitText_Disabling_PersistsSuppressionAndNormalizedText()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", true, "12345678901");
        var service = Service(dbFactory, maximumLength: 10);

        var outcome = await service.SaveAsync(
            Session(hostId, AuthRole.Streamer),
            hostId,
            new StartupMessageSaveCommand(false, "  12345678901  "),
            CancellationToken.None
        );

        outcome
            .ShouldBeOfType<StartupMessageSaveOutcome.Saved>()
            .Configuration.ShouldBe(new StartupMessageConfiguration(false, "12345678901"));
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.Hosts.SingleAsync(x => x.Id == hostId);
        stored.StartupMessageEnabled.ShouldBe(false);
        stored.StartupMessageText.ShouldBe("12345678901");
        _ = (
            await service.GetAsync("streamer", CancellationToken.None)
        ).ShouldBeOfType<StartupChatMessage.Disabled>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task IncompleteEnabledOverride_LoadingEffectiveMessage_SafelySuppressesDelivery(
        string? storedText
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedHostAsync(dbFactory, "incomplete", true, storedText);
        var service = Service(dbFactory);

        var effective = await service.GetAsync("incomplete", CancellationToken.None);

        _ = effective.ShouldBeOfType<StartupChatMessage.Disabled>();
    }

    private static StartupMessageConfigurationService Service(
        SqliteBlokeBotDbFactory dbFactory,
        int maximumLength = 500
    ) =>
        new(
            dbFactory,
            BotSettings.FromOptions(
                new BotOptions
                {
                    StartupMessage = "Beep boop.",
                    MaxChatMessageLength = maximumLength,
                }
            )
        );

    private static AuthenticatedSession Session(int hostId, AuthRole role)
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", role);
        return new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = role == AuthRole.Admin ? "admin-id" : "streamer-id",
            Login = role == AuthRole.Admin ? "administrator" : "streamer",
            IsBotAdmin = role == AuthRole.Admin,
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login,
        bool? enabled,
        string? text
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            StartupMessageEnabled = enabled,
            StartupMessageText = text,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
