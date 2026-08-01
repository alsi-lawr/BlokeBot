using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using BlokeBot.Testing;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutDeliveryTests
{
    [Test]
    [Arguments("sent", AutomaticRaidShoutoutResultCode.Delivered)]
    [Arguments("cooldown", AutomaticRaidShoutoutResultCode.Cooldown)]
    [Arguments("authority", AutomaticRaidShoutoutResultCode.AuthorityRequired)]
    [Arguments("invalid", AutomaticRaidShoutoutResultCode.Invalid)]
    [Arguments("rejected", AutomaticRaidShoutoutResultCode.Rejected)]
    [Arguments("ambiguous", AutomaticRaidShoutoutResultCode.Ambiguous)]
    public async Task NativeAdapter_MapsManualOperationOutcomeWithoutChatFallback(
        string scenario,
        AutomaticRaidShoutoutResultCode expectedCode
    )
    {
        var native = new AutomaticRaidNativeShoutoutSender(
            new ScriptedNativeOperation(NativeOutcome(scenario))
        );

        var result = await native.SendAsync(1, "raider", CancellationToken.None);

        var code = result switch
        {
            AutomaticRaidShoutoutDeliveryResult.Delivered =>
                AutomaticRaidShoutoutResultCode.Delivered,
            AutomaticRaidShoutoutDeliveryResult.NotDelivered notDelivered => notDelivered.Reason,
            AutomaticRaidShoutoutDeliveryResult.Ambiguous =>
                AutomaticRaidShoutoutResultCode.Ambiguous,
            _ => throw new InvalidOperationException(),
        };
        code.ShouldBe(expectedCode);
    }

    [Test]
    public async Task NativeAdapter_MapsProductionUnauthorizedAuthorityOutcome()
    {
        const string ProductionMessage = "Twitch rejected the configured bot's shoutout authority.";
        ProductionMessage.ShouldBe(ShoutoutService.UnauthorizedAuthorityMessage);
        var native = new AutomaticRaidNativeShoutoutSender(
            new ScriptedNativeOperation(new ShoutoutOperationOutcome.NotReady(ProductionMessage))
        );

        var result = await native.SendAsync(1, "raider", CancellationToken.None);

        result
            .ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.NotDelivered>()
            .Reason.ShouldBe(AutomaticRaidShoutoutResultCode.AuthorityRequired);
    }

    [Test]
    public async Task NativeCooldown_UsesOnlyNativeMechanismAndCreatesOneHostAlert()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(database);
        var native = new RecordingNative(
            new AutomaticRaidShoutoutDeliveryResult.NotDelivered(
                AutomaticRaidShoutoutResultCode.Cooldown
            )
        );
        var chat = new RecordingChat();
        var announcement = new RecordingAnnouncement();
        var delivery = Delivery(database, native, new UnavailableInformation(), chat, announcement);

        var result = await delivery.DeliverAsync(
            Request(Configuration(AutomaticRaidShoutoutMechanism.Native)),
            CancellationToken.None
        );

        result
            .ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.NotDelivered>()
            .Reason.ShouldBe(AutomaticRaidShoutoutResultCode.Cooldown);
        native.Calls.ShouldBe(1);
        chat.Calls.ShouldBeEmpty();
        announcement.Calls.ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        var alert = await verify.DurableAlerts.SingleAsync();
        alert.HostId.ShouldBe(1);
        alert.SourceKey.ShouldBe("raid-message");
    }

    [Test]
    public async Task RegularChat_EnrichmentFailureUsesInlineFallbackAndOneCorrelatedMessage()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(database);
        var chat = new RecordingChat();
        var delivery = Delivery(
            database,
            new RecordingNative(new AutomaticRaidShoutoutDeliveryResult.Delivered()),
            new UnavailableInformation(),
            chat,
            new RecordingAnnouncement()
        );
        var configuration = Configuration(
            AutomaticRaidShoutoutMechanism.Chat,
            AutomaticRaidChatPresentation.Regular,
            "{twitch_handle}|{last_game|unknown game}|{stream_title|untitled}"
        );

        var result = await delivery.DeliverAsync(Request(configuration), CancellationToken.None);

        result.ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.Delivered>();
        var call = chat.Calls.ShouldHaveSingleItem();
        call.Message.ShouldBe("@raider|unknown game|untitled");
        call.Correlation.ShouldBe(new PublicChatDeliveryCorrelation(1, "raid-message"));
        call.PinIntent.ShouldBeNull();
    }

    [Test]
    public async Task RenderedMessageAbove500_IsRejectedBeforeAnyPresentation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(database);
        var chat = new RecordingChat();
        var announcement = new RecordingAnnouncement();
        var delivery = Delivery(
            database,
            new RecordingNative(new AutomaticRaidShoutoutDeliveryResult.Delivered()),
            new FoundInformation("Game", "Title"),
            chat,
            announcement
        );
        var request = Request(
            Configuration(
                AutomaticRaidShoutoutMechanism.Chat,
                AutomaticRaidChatPresentation.Regular,
                "{display_name}"
            )
        ) with
        {
            RaiderDisplayName = new string('x', 501),
        };

        var result = await delivery.DeliverAsync(request, CancellationToken.None);

        result
            .ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.NotDelivered>()
            .Reason.ShouldBe(AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong);
        chat.Calls.ShouldBeEmpty();
        announcement.Calls.ShouldBeEmpty();
    }

    [Test]
    [Arguments(120)]
    [Arguments(null)]
    public async Task PinnedChat_QueuesOneMessageWithDurableRaidOwnerAndNoCleanup(
        int? durationSeconds
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAndOutcomeAsync(database);
        var chat = new RecordingChat();
        var delivery = Delivery(
            database,
            new RecordingNative(new AutomaticRaidShoutoutDeliveryResult.Delivered()),
            new FoundInformation("Game", "Title"),
            chat,
            new RecordingAnnouncement()
        );
        var configuration = Configuration(
            AutomaticRaidShoutoutMechanism.Chat,
            AutomaticRaidChatPresentation.Pinned
        ) with
        {
            PinDurationSeconds = durationSeconds,
        };

        var result = await delivery.DeliverAsync(Request(configuration), CancellationToken.None);

        result.ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.Delivered>();
        var call = chat.Calls.ShouldHaveSingleItem();
        var pin = call.PinIntent.ShouldNotBeNull();
        pin.HostId.ShouldBe(1);
        pin.OwnerId.ShouldBe(1);
        pin.Feature.ShouldBe(AutomaticRaidDeliveryCorrelation.Feature);
        pin.ReplyKey.ShouldBe("raid-message");
        pin.DurationSeconds.ShouldBe(durationSeconds);
        pin.UnpinOnOwnerCompletion.ShouldBeFalse();
    }

    [Test]
    [Arguments(PersistedAnnouncementColor.Primary)]
    [Arguments(PersistedAnnouncementColor.Blue)]
    [Arguments(PersistedAnnouncementColor.Green)]
    [Arguments(PersistedAnnouncementColor.Orange)]
    [Arguments(PersistedAnnouncementColor.Purple)]
    public async Task Announcement_UsesSelectedColorOnceWithoutChatFallback(
        PersistedAnnouncementColor color
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(database);
        var chat = new RecordingChat();
        var announcement = new RecordingAnnouncement();
        var delivery = Delivery(
            database,
            new RecordingNative(new AutomaticRaidShoutoutDeliveryResult.Delivered()),
            new FoundInformation("Game", "Title"),
            chat,
            announcement
        );
        var configuration = Configuration(
            AutomaticRaidShoutoutMechanism.Chat,
            AutomaticRaidChatPresentation.Announcement
        ) with
        {
            AnnouncementColor = color,
        };

        var result = await delivery.DeliverAsync(Request(configuration), CancellationToken.None);

        result.ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.Delivered>();
        announcement.Calls.ShouldHaveSingleItem().Color.ShouldBe(color);
        chat.Calls.ShouldBeEmpty();
    }

    [Test]
    [Arguments("authority", AutomaticRaidShoutoutResultCode.AuthorityRequired)]
    [Arguments("not-ready", AutomaticRaidShoutoutResultCode.NotReady)]
    [Arguments("invalid", AutomaticRaidShoutoutResultCode.Invalid)]
    [Arguments("rate-limited", AutomaticRaidShoutoutResultCode.RateLimited)]
    [Arguments("rejected", AutomaticRaidShoutoutResultCode.Rejected)]
    [Arguments("unexpected", AutomaticRaidShoutoutResultCode.Unexpected)]
    public async Task AnnouncementTerminalFailure_MapsOnceWithoutRetryOrChatFallback(
        string scenario,
        AutomaticRaidShoutoutResultCode expectedCode
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(database);
        var chat = new RecordingChat();
        var announcement = new RecordingAnnouncement(AnnouncementResult(scenario));
        var delivery = Delivery(
            database,
            new RecordingNative(new AutomaticRaidShoutoutDeliveryResult.Delivered()),
            new FoundInformation("Game", "Title"),
            chat,
            announcement
        );

        var result = await delivery.DeliverAsync(
            Request(
                Configuration(
                    AutomaticRaidShoutoutMechanism.Chat,
                    AutomaticRaidChatPresentation.Announcement
                )
            ),
            CancellationToken.None
        );

        result
            .ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.NotDelivered>()
            .Reason.ShouldBe(expectedCode);
        announcement.Calls.Count.ShouldBe(1);
        chat.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task AnnouncementAmbiguous_IsReportedOnceWithoutRetryOrChatFallback()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(database);
        var chat = new RecordingChat();
        var announcement = new RecordingAnnouncement(
            new AutomaticRaidAnnouncementSendResult.Ambiguous()
        );
        var delivery = Delivery(
            database,
            new RecordingNative(new AutomaticRaidShoutoutDeliveryResult.Delivered()),
            new FoundInformation("Game", "Title"),
            chat,
            announcement
        );

        var result = await delivery.DeliverAsync(
            Request(
                Configuration(
                    AutomaticRaidShoutoutMechanism.Chat,
                    AutomaticRaidChatPresentation.Announcement
                )
            ),
            CancellationToken.None
        );

        result.ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.Ambiguous>();
        announcement.Calls.Count.ShouldBe(1);
        chat.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task RegularChatAdmissionRejection_DoesNotRetryOrFallBackToAnnouncement()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(database);
        var chat = new RecordingChat(new PublicChatSendOutcome.Rejected());
        var announcement = new RecordingAnnouncement();
        var delivery = Delivery(
            database,
            new RecordingNative(new AutomaticRaidShoutoutDeliveryResult.Delivered()),
            new FoundInformation("Game", "Title"),
            chat,
            announcement
        );

        var result = await delivery.DeliverAsync(
            Request(Configuration(AutomaticRaidShoutoutMechanism.Chat)),
            CancellationToken.None
        );

        result
            .ShouldBeOfType<AutomaticRaidShoutoutDeliveryResult.NotDelivered>()
            .Reason.ShouldBe(AutomaticRaidShoutoutResultCode.Rejected);
        chat.Calls.Count.ShouldBe(1);
        announcement.Calls.ShouldBeEmpty();
    }

    private static AutomaticRaidShoutoutDelivery Delivery(
        SqliteBlokeBotDbFactory database,
        IAutomaticRaidNativeShoutoutSender native,
        IAutomaticRaidChannelInformationProvider information,
        IPublicChatMessageSender chat,
        IAutomaticRaidAnnouncementSender announcements
    ) =>
        new(
            native,
            information,
            chat,
            announcements,
            database,
            new DurableAlertService(
                database,
                TimeProvider.System,
                TestEventBus.Create<AppEventKind>()
            )
        );

    private static AutomaticRaidShoutoutDeliveryRequest Request(
        AutomaticRaidShoutoutConfiguration configuration
    ) =>
        new(
            1,
            "host",
            configuration,
            "raid-message",
            DateTimeOffset.UtcNow,
            "raider-id",
            "raider",
            "Raider",
            42
        );

    private static AutomaticRaidShoutoutConfiguration Configuration(
        AutomaticRaidShoutoutMechanism mechanism,
        AutomaticRaidChatPresentation presentation = AutomaticRaidChatPresentation.Regular,
        string template = "Welcome {display_name}"
    ) => new(true, 1, mechanism, presentation, template, null, PersistedAnnouncementColor.Primary);

    private static async Task SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        db.Hosts.Add(
            new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All,
                Login = "host",
                DisplayName = "Host",
                TwitchUserId = "host-id",
            }
        );
        await db.SaveChangesAsync();
    }

    private static AutomaticRaidAnnouncementSendResult AnnouncementResult(string scenario) =>
        scenario switch
        {
            "authority" => new AutomaticRaidAnnouncementSendResult.AuthorityRequired(),
            "not-ready" => new AutomaticRaidAnnouncementSendResult.NotReady(),
            "invalid" => new AutomaticRaidAnnouncementSendResult.Invalid(),
            "rate-limited" => new AutomaticRaidAnnouncementSendResult.RateLimited(),
            "rejected" => new AutomaticRaidAnnouncementSendResult.Rejected(),
            "unexpected" => new AutomaticRaidAnnouncementSendResult.Unexpected(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static ShoutoutOperationOutcome NativeOutcome(string scenario) =>
        scenario switch
        {
            "sent" => new ShoutoutOperationOutcome.Sent("raider"),
            "cooldown" => new ShoutoutOperationOutcome.CooldownUnknown(),
            "authority" => new ShoutoutOperationOutcome.NotReady(
                "Reconnect the bot account with shoutout permissions."
            ),
            "invalid" => new ShoutoutOperationOutcome.TargetNotFound("raider"),
            "rejected" => new ShoutoutOperationOutcome.ProviderRejected(
                "Twitch rejected that shoutout target."
            ),
            "ambiguous" => new ShoutoutOperationOutcome.ProviderRejected(
                "Twitch could not confirm the shoutout."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static async Task SeedHostAndOutcomeAsync(SqliteBlokeBotDbFactory database)
    {
        await SeedHostAsync(database);
        await using var db = await database.CreateDbContextAsync();
        db.AutomaticRaidShoutoutOutcomes.Add(
            new AutomaticRaidShoutoutOutcome
            {
                HostId = 1,
                ProviderMessageId = "raid-message",
                SourceTwitchUserId = "raider-id",
                SourceLogin = "raider",
                SourceDisplayName = "Raider",
                ViewerCount = 42,
                MessageTimestampUtc = DateTime.UtcNow,
                ClaimedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private sealed class RecordingNative(AutomaticRaidShoutoutDeliveryResult result)
        : IAutomaticRaidNativeShoutoutSender
    {
        public int Calls { get; private set; }

        public Task<AutomaticRaidShoutoutDeliveryResult> SendAsync(
            int hostId,
            string targetLogin,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class ScriptedNativeOperation(ShoutoutOperationOutcome outcome)
        : IAutomaticRaidNativeShoutoutOperation
    {
        public Task<ShoutoutOperationOutcome> SendAsync(
            int hostId,
            string targetLogin,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(outcome);
        }
    }

    private sealed class FoundInformation(string? gameName, string? title)
        : IAutomaticRaidChannelInformationProvider
    {
        public Task<AutomaticRaidChannelInformationResult> GetAsync(
            string raiderTwitchUserId,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AutomaticRaidChannelInformationResult>(
                new AutomaticRaidChannelInformationResult.Found(gameName, title)
            );
        }
    }

    private sealed class UnavailableInformation : IAutomaticRaidChannelInformationProvider
    {
        public Task<AutomaticRaidChannelInformationResult> GetAsync(
            string raiderTwitchUserId,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AutomaticRaidChannelInformationResult>(
                new AutomaticRaidChannelInformationResult.Unavailable()
            );
        }
    }

    private sealed class RecordingChat(PublicChatSendOutcome? result = null)
        : IPublicChatMessageSender
    {
        public List<ChatCall> Calls { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Automatic delivery must use correlation.");

        public ValueTask<PublicChatSendOutcome> SendCorrelatedAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            PublicChatDeliveryCorrelation correlation,
            CancellationToken cancellationToken
        ) => Record(channel, message, deadline, correlation, null, cancellationToken);

        public ValueTask<PublicChatSendOutcome> SendCorrelatedAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            PublicChatDeliveryCorrelation correlation,
            PublicChatPinIntent pinIntent,
            CancellationToken cancellationToken
        ) => Record(channel, message, deadline, correlation, pinIntent, cancellationToken);

        private ValueTask<PublicChatSendOutcome> Record(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            PublicChatDeliveryCorrelation correlation,
            PublicChatPinIntent? pinIntent,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new(channel, message, deadline, correlation, pinIntent));
            return ValueTask.FromResult<PublicChatSendOutcome>(
                result ?? new PublicChatSendOutcome.Accepted()
            );
        }
    }

    private sealed class RecordingAnnouncement(AutomaticRaidAnnouncementSendResult? result = null)
        : IAutomaticRaidAnnouncementSender
    {
        public List<AnnouncementCall> Calls { get; } = [];

        public Task<AutomaticRaidAnnouncementSendResult> SendAsync(
            string channelLogin,
            string message,
            PersistedAnnouncementColor color,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new(channelLogin, message, color));
            return Task.FromResult<AutomaticRaidAnnouncementSendResult>(
                result ?? new AutomaticRaidAnnouncementSendResult.Sent()
            );
        }
    }

    private sealed record ChatCall(
        string Channel,
        string Message,
        PublicChatDeliveryDeadline Deadline,
        PublicChatDeliveryCorrelation Correlation,
        PublicChatPinIntent? PinIntent
    );

    private sealed record AnnouncementCall(
        string Channel,
        string Message,
        PersistedAnnouncementColor Color
    );
}
