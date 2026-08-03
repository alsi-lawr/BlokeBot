using BlokeBot.Core.Features.HostedChannels.Whispers;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class WhisperQuotaTests : WhisperResponseTestBase
{
    [Test]
    public async Task SameRecipientSameDay_ReservingQuota_ReturnsNewThenExistingRecipient()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = CreateQuota(dbFactory);

        var first = await quota
            .ReserveRecipient(hostId, "bot-id", "viewer-id", "viewer")
            .ExecuteAsync(CancellationToken.None);
        var second = await quota
            .ReserveRecipient(hostId, "bot-id", "viewer-id", "Viewer")
            .ExecuteAsync(CancellationToken.None);

        _ = first.Match(
            static success => success.ShouldBeOfType<WhisperQuotaReservation.NewRecipient>(),
            static _ => throw new InvalidOperationException("Expected a successful reservation.")
        );
        var existing = second.Match(
            static success => success.ShouldBeOfType<WhisperQuotaReservation.ExistingRecipient>(),
            static _ => throw new InvalidOperationException("Expected a successful reservation.")
        );
        existing.Status.RecipientCount.ShouldBe(1);
    }

    [Test]
    public async Task QuotaReservation_Construction_DefersPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = CreateQuota(dbFactory);

        var reservation = quota.ReserveRecipient(hostId, "bot-id", "viewer-id", "viewer");
        var beforeExecution = await quota.GetStatusAsync(hostId, "bot-id", CancellationToken.None);
        var result = await reservation.ExecuteAsync(CancellationToken.None);

        beforeExecution.RecipientCount.ShouldBe(0);
        _ = result.Match(
            static success => success.ShouldBeOfType<WhisperQuotaReservation.NewRecipient>(),
            static _ => throw new InvalidOperationException("Expected a successful reservation.")
        );
    }

    [Test]
    public async Task InvalidIdentity_ReservingQuota_ReturnsTypedErrorWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = CreateQuota(dbFactory);

        var result = await quota
            .ReserveRecipient(hostId, " ", "viewer-id", "viewer")
            .ExecuteAsync(CancellationToken.None);

        _ = result.Match(
            static _ => throw new InvalidOperationException("Expected an invalid identity error."),
            static error => error.ShouldBeOfType<WhisperQuotaReservationError.InvalidIdentity>()
        );
        (
            await quota.GetStatusAsync(hostId, "bot-id", CancellationToken.None)
        ).RecipientCount.ShouldBe(0);
    }

    [Test]
    public async Task QuotaAtLimit_ReservingExistingAndNewRecipient_ReturnsTypedCases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = CreateQuota(dbFactory);

        for (var index = 0; index < WhisperQuotaService.UniqueRecipientLimit; index++)
        {
            var result = await quota
                .ReserveRecipient(hostId, "bot-id", $"viewer-id-{index}", $"viewer{index}")
                .ExecuteAsync(CancellationToken.None);
            _ = result.Match(
                static success => success.ShouldBeOfType<WhisperQuotaReservation.NewRecipient>(),
                static _ =>
                    throw new InvalidOperationException("Expected a successful reservation.")
            );
        }

        var blocked = await quota
            .ReserveRecipient(hostId, "bot-id", "viewer-id-40", "viewer40")
            .ExecuteAsync(CancellationToken.None);
        var existing = await quota
            .ReserveRecipient(hostId, "bot-id", "viewer-id-0", "viewer0")
            .ExecuteAsync(CancellationToken.None);

        var limit = blocked.Match(
            static _ => throw new InvalidOperationException("Expected a quota error."),
            static error =>
                error.ShouldBeOfType<WhisperQuotaReservationError.DailyRecipientLimitReached>()
        );
        limit.Status.RecipientCount.ShouldBe(WhisperQuotaService.UniqueRecipientLimit);
        limit.Status.Exhausted.ShouldBeTrue();
        existing
            .Match(
                static success => success,
                static _ => throw new InvalidOperationException("Expected an existing recipient.")
            )
            .ShouldBeOfType<WhisperQuotaReservation.ExistingRecipient>()
            .Status.Exhausted.ShouldBeTrue();
    }
}
