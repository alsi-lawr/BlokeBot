using System.Net;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Identity;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class WhisperDeliveryTests : WhisperResponseTestBase
{
    [Test]
    public async Task Delivery_Construction_DefersTokenQuotaAndHelixIo()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);

        var delivery = harness.Sender.Deliver(harness.Source(), "your balance is 10");
        var statusBeforeExecution = await harness.Quota.GetStatusAsync(
            harness.HostId,
            "custom-id",
            CancellationToken.None
        );

        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
        statusBeforeExecution.RecipientCount.ShouldBe(0);

        var result = await delivery.ExecuteAsync(CancellationToken.None);

        var receipt = result.Match(
            receipt => receipt,
            _ => throw new InvalidOperationException("Expected private delivery success.")
        );
        receipt.ShouldBe(new PrivateDeliveryReceipt());
        harness.Http.ValidationRequestCount.ShouldBe(1);
        harness.Http.WhisperRequestCount.ShouldBe(1);
        harness.FailureHandler.Failures.ShouldBeEmpty();
        harness.Chat.Messages.ShouldBeEmpty();
        (
            await harness.Quota.GetStatusAsync(harness.HostId, "custom-id", CancellationToken.None)
        ).RecipientCount.ShouldBe(1);
    }

    [Test]
    public async Task DisabledWhispers_Delivering_ReturnsDisabledWithoutTokenOrHelixIo()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            whisperResponsesEnabled: false
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source());

        error.ShouldBeOfType<PrivateDeliveryError.Disabled>();
        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task InvalidBotToken_Delivering_ReturnsSenderIdentityUnavailable()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            validationAccepted: false
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source());

        error.ShouldBeOfType<PrivateDeliveryError.SenderIdentityUnavailable>();
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task MissingRecipient_Delivering_ReturnsRecipientUnavailable()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            usersJson: """{"data":[]}"""
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source(includeUserId: false));

        error.ShouldBeOfType<PrivateDeliveryError.RecipientUnavailable>();
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task SelfRecipient_Delivering_ReturnsSelfRecipientWithoutQuotaOrHelixIo()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);

        var error = await SendPrivateFailureAsync(harness, harness.Source(userId: "custom-id"));

        error.ShouldBeOfType<PrivateDeliveryError.SelfRecipient>();
        harness.Http.WhisperRequestCount.ShouldBe(0);
        (
            await harness.Quota.GetStatusAsync(harness.HostId, "custom-id", CancellationToken.None)
        ).RecipientCount.ShouldBe(0);
    }

    [Test]
    public async Task ExhaustedQuota_Delivering_ReturnsQuotaExceededWithoutHelixIo()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);
        for (var index = 0; index < WhisperQuotaService.UniqueRecipientLimit; index++)
        {
            _ = await harness
                .Quota.ReserveRecipient(
                    harness.HostId,
                    "custom-id",
                    $"recipient-{index}",
                    $"viewer{index}"
                )
                .ExecuteAsync(CancellationToken.None);
        }

        var error = await SendPrivateFailureAsync(harness, harness.Source());

        var quota = error.ShouldBeOfType<PrivateDeliveryError.QuotaExceeded>();
        quota.Status.Exhausted.ShouldBeTrue();
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task ProviderRateLimit_Delivering_ReturnsRateLimitedAndExhaustsQuota()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.TooManyRequests,
            whisperBody: "sensitive provider response"
        );

        var error = await SendPrivateFailureAsync(
            harness,
            harness.Source(),
            "sensitive private message"
        );

        var rateLimited = error.ShouldBeOfType<PrivateDeliveryError.RateLimited>();
        rateLimited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rateLimited.ToString().ShouldNotContain("sensitive");
        (
            await harness.Quota.GetStatusAsync(harness.HostId, "custom-id", CancellationToken.None)
        ).Exhausted.ShouldBeTrue();
    }

    [Test]
    public async Task RejectedWhisper_Delivering_ReturnsRedactedStatus()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.BadRequest,
            whisperBody: "sensitive provider response"
        );

        var error = await SendPrivateFailureAsync(
            harness,
            harness.Source(),
            "sensitive private message"
        );

        var rejected = error.ShouldBeOfType<PrivateDeliveryError.Rejected>();
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        rejected.ToString().ShouldNotContain("sensitive");
    }

    [Test]
    public async Task RecipientLookupTransportFailure_Delivering_ReturnsTransientWithCause()
    {
        var cause = new HttpRequestException("sensitive lookup failure");
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            usersException: cause
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source(includeUserId: false));

        var transient = error.ShouldBeOfType<PrivateDeliveryError.Transient>();
        transient.Cause.ShouldBeSameAs(cause);
        transient.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
        transient.ToString().ShouldNotContain("sensitive lookup failure");
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task WhisperTransportFailure_Delivering_ReturnsAmbiguousWithCause()
    {
        var cause = new IOException("sensitive send failure");
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            whisperException: cause
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source());

        var ambiguous = error.ShouldBeOfType<PrivateDeliveryError.Ambiguous>();
        ambiguous.Cause.ShouldBeSameAs(cause);
        ambiguous.FailureType.ShouldBe(typeof(IOException).FullName);
        ambiguous.ToString().ShouldNotContain("sensitive send failure");
    }

    [Test]
    public async Task UnexpectedPreparationFailure_Delivering_PreservesRedactedCause()
    {
        var cause = new SensitiveWhisperException("sensitive unexpected failure");
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            usersException: cause
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source(includeUserId: false));

        var unexpected = error.ShouldBeOfType<PrivateDeliveryError.Unexpected>();
        unexpected.Cause.ShouldBeSameAs(cause);
        unexpected.FailureType.ShouldBe(typeof(SensitiveWhisperException).FullName);
        unexpected.ToString().ShouldNotContain("sensitive unexpected failure");
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task CallerCancellation_Delivering_PropagatesCancellation()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () =>
            await harness
                .Sender.Deliver(harness.Source(), "message")
                .ExecuteAsync(cancellation.Token);

        await action.ShouldThrowAsync<OperationCanceledException>();
        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
        harness.FailureHandler.Failures.ShouldBeEmpty();
        harness.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCancellationDuringWhisper_Delivering_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            cancelOnWhisper: cancellation
        );

        var action = async () =>
            await harness
                .Sender.Deliver(harness.Source(), "message")
                .ExecuteAsync(cancellation.Token);

        await action.ShouldThrowAsync<OperationCanceledException>();
        harness.Http.WhisperRequestCount.ShouldBe(1);
        harness.FailureHandler.Failures.ShouldBeEmpty();
        harness.Chat.Messages.ShouldBeEmpty();
    }
}
