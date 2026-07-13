using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.HostedChannels.Whispers;

public sealed class HostWhisperCommandResponseSender(
    ITwitchChatMessageSender chat,
    HostBotAccountAuthorizationService botAccounts,
    HostWhisperQuotaService quota,
    HelixClient users,
    WhisperClient whispers,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TwitchBotIdentity identity,
    IPrivateDeliveryFailureHandler failureHandler,
    ILogger<HostWhisperCommandResponseSender> log
) : ITwitchCommandResponseSender
{
    public async ValueTask SendAsync(
        TwitchChatMessage sourceMessage,
        TwitchCommandResponse response,
        CancellationToken cancellationToken
    )
    {
        if (response.Target != TwitchCommandResponseTarget.Whisper)
        {
            await SendPublicChatAsync(sourceMessage.Channel, response.Message, cancellationToken);
            return;
        }

        var result = await Deliver(sourceMessage, response.Message).ExecuteAsync(cancellationToken);
        await result.Match(
            _ => ValueTask.CompletedTask,
            error => HandlePrivateDeliveryErrorAsync(sourceMessage, error, cancellationToken)
        );
    }

    public IO<PrivateDeliveryReceipt, PrivateDeliveryError> Deliver(
        TwitchChatMessage sourceMessage,
        string message
    )
    {
        ArgumentNullException.ThrowIfNull(sourceMessage);
        ArgumentNullException.ThrowIfNull(message);
        return IO<PrivateDeliveryReceipt, PrivateDeliveryError>.Create(cancellationToken =>
            DeliverAsync(sourceMessage, message, cancellationToken)
        );
    }

    private async ValueTask<Result<PrivateDeliveryReceipt, PrivateDeliveryError>> DeliverAsync(
        TwitchChatMessage sourceMessage,
        string message,
        CancellationToken cancellationToken
    )
    {
        PreparedPrivateDelivery prepared;
        try
        {
            var preparation = await PrepareAsync(sourceMessage, cancellationToken);
            if (preparation is PrivateDeliveryPreparation.Failed failed)
            {
                return Error(failed.Error);
            }

            prepared = preparation switch
            {
                PrivateDeliveryPreparation.Ready ready => ready.Delivery,
                _ => throw new UnreachableException("Unknown private-delivery preparation."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return Error(new PrivateDeliveryError.Transient(exception));
        }
        catch (IOException exception)
        {
            return Error(new PrivateDeliveryError.Transient(exception));
        }
        catch (JsonException exception)
        {
            return Error(new PrivateDeliveryError.Transient(exception));
        }
        catch (TimeoutException exception)
        {
            return Error(new PrivateDeliveryError.Transient(exception));
        }
        catch (OperationCanceledException exception)
        {
            return Error(new PrivateDeliveryError.Transient(exception));
        }
        catch (Exception exception)
        {
            return Error(new PrivateDeliveryError.Unexpected(exception));
        }

        try
        {
            var result = await whispers.SendAsync(
                new HelixRequestContext(identity.ClientId, prepared.AccessToken),
                prepared.SenderUserId,
                prepared.RecipientUserId,
                message,
                cancellationToken
            );
            return result.Status switch
            {
                WhisperSendStatus.Accepted => Success(),
                WhisperSendStatus.RateLimited => await RateLimitedAsync(
                    prepared,
                    result.StatusCode,
                    cancellationToken
                ),
                WhisperSendStatus.Rejected => Error(
                    new PrivateDeliveryError.Rejected(result.StatusCode)
                ),
                _ => throw new UnreachableException("Unknown Twitch whisper send status."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return Error(new PrivateDeliveryError.Ambiguous(exception));
        }
        catch (IOException exception)
        {
            return Error(new PrivateDeliveryError.Ambiguous(exception));
        }
        catch (JsonException exception)
        {
            return Error(new PrivateDeliveryError.Ambiguous(exception));
        }
        catch (TimeoutException exception)
        {
            return Error(new PrivateDeliveryError.Ambiguous(exception));
        }
        catch (OperationCanceledException exception)
        {
            return Error(new PrivateDeliveryError.Ambiguous(exception));
        }
        catch (Exception exception)
        {
            return Error(new PrivateDeliveryError.Unexpected(exception));
        }
    }

    private async ValueTask<PrivateDeliveryPreparation> PrepareAsync(
        TwitchChatMessage sourceMessage,
        CancellationToken cancellationToken
    )
    {
        var host = await ResolveHostAsync(sourceMessage.Channel, cancellationToken);
        if (host is null)
        {
            return new PrivateDeliveryPreparation.Failed(new PrivateDeliveryError.Disabled());
        }

        var tokenStatus = await botAccounts.GetCustomBotTokenStatusAsync(
            host.Id,
            [Scopes.UserManageWhispers],
            cancellationToken
        );
        var readyStatus = tokenStatus.Status.Match<TwitchTokenStatus.Ready?>(
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            ready => ready
        );
        if (readyStatus is null)
        {
            return new PrivateDeliveryPreparation.Failed(
                new PrivateDeliveryError.SenderIdentityUnavailable()
            );
        }

        var recipientUserId = await ResolveRecipientUserIdAsync(
            sourceMessage,
            readyStatus.AccessToken,
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(recipientUserId))
        {
            return new PrivateDeliveryPreparation.Failed(
                new PrivateDeliveryError.RecipientUnavailable()
            );
        }

        var senderUserId = readyStatus.Validation.UserId;
        if (string.Equals(senderUserId, recipientUserId, StringComparison.Ordinal))
        {
            return new PrivateDeliveryPreparation.Failed(new PrivateDeliveryError.SelfRecipient());
        }

        var reservation = await quota
            .ReserveRecipient(host.Id, senderUserId, recipientUserId, sourceMessage.Login)
            .ExecuteAsync(cancellationToken);
        return reservation.Match<PrivateDeliveryPreparation>(
            _ => new PrivateDeliveryPreparation.Ready(
                new PreparedPrivateDelivery(
                    host.Id,
                    readyStatus.AccessToken,
                    senderUserId,
                    recipientUserId
                )
            ),
            error =>
                error switch
                {
                    WhisperQuotaReservationError.InvalidIdentity =>
                        new PrivateDeliveryPreparation.Failed(
                            new PrivateDeliveryError.SenderIdentityUnavailable()
                        ),
                    WhisperQuotaReservationError.DailyRecipientLimitReached limit =>
                        new PrivateDeliveryPreparation.Failed(
                            new PrivateDeliveryError.QuotaExceeded(limit.Status)
                        ),
                    _ => throw new UnreachableException("Unknown whisper quota reservation error."),
                }
        );
    }

    private async ValueTask<Result<PrivateDeliveryReceipt, PrivateDeliveryError>> RateLimitedAsync(
        PreparedPrivateDelivery prepared,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken
    )
    {
        await quota.MarkExhaustedAsync(prepared.HostId, prepared.SenderUserId, cancellationToken);
        return Error(new PrivateDeliveryError.RateLimited(statusCode));
    }

    private async ValueTask HandlePrivateDeliveryErrorAsync(
        TwitchChatMessage sourceMessage,
        PrivateDeliveryError error,
        CancellationToken cancellationToken
    )
    {
        var context = new PrivateDeliveryFailureContext
        {
            HostChannel = Login.Normalize(sourceMessage.Channel),
        };
        var handling = error switch
        {
            PrivateDeliveryError.Disabled => HandleFailureAsync(error, context, cancellationToken),
            PrivateDeliveryError.SenderIdentityUnavailable => HandleFailureAsync(
                error,
                context,
                cancellationToken
            ),
            PrivateDeliveryError.RecipientUnavailable => HandleFailureAsync(
                error,
                context,
                cancellationToken
            ),
            PrivateDeliveryError.SelfRecipient => HandleFailureAsync(
                error,
                context,
                cancellationToken
            ),
            PrivateDeliveryError.QuotaExceeded => HandleFailureAsync(
                error,
                context,
                cancellationToken
            ),
            PrivateDeliveryError.RateLimited => HandleFailureAsync(
                error,
                context,
                cancellationToken
            ),
            PrivateDeliveryError.Transient => HandleFailureAsync(error, context, cancellationToken),
            PrivateDeliveryError.Rejected => HandleFailureAsync(error, context, cancellationToken),
            PrivateDeliveryError.Ambiguous => HandleFailureAsync(error, context, cancellationToken),
            PrivateDeliveryError.Unexpected => HandleFailureAsync(
                error,
                context,
                cancellationToken
            ),
            _ => throw new UnreachableException("Unknown private-delivery error."),
        };
        await handling;
    }

    private async ValueTask HandleFailureAsync(
        PrivateDeliveryError error,
        PrivateDeliveryFailureContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await failureHandler.HandleAsync(error, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PrivateDeliveryFailureHandlingException(error, context, exception);
        }
    }

    private async Task SendPublicChatAsync(
        string channel,
        string message,
        CancellationToken cancellationToken
    )
    {
        var outcome = await chat.SendAsync(
            channel,
            message,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        outcome
            .Match<Action>(
                static _ => static () => { },
                _ =>
                    () =>
                        log.LogWarning(
                            "Hosted public command response for channel #{Channel} was rejected before durable enqueue; no user-visible delivery was attempted.",
                            Login.Normalize(channel)
                        )
            )
            .Invoke();
    }

    private async Task<WhisperHost?> ResolveHostAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        var login = Login.Normalize(channel);
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == login)
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (host is null)
        {
            return null;
        }

        var enabled = await db
            .HostBotAccountSettings.AsNoTracking()
            .Where(x => x.HostId == host.Id)
            .Select(x => x.OverrideEnabled && x.WhisperResponsesEnabled)
            .SingleOrDefaultAsync(cancellationToken);

        return enabled ? new WhisperHost(host.Id) : null;
    }

    private async Task<string?> ResolveRecipientUserIdAsync(
        TwitchChatMessage sourceMessage,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        if (
            sourceMessage.Tags.TryGetValue("user-id", out var taggedUserId)
            && !string.IsNullOrWhiteSpace(taggedUserId)
        )
        {
            return taggedUserId.Trim();
        }

        var login = Login.Normalize(sourceMessage.Login);
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        var resolved = await users.GetUsersByLoginAsync(
            new HelixRequestContext(identity.ClientId, accessToken),
            [login],
            cancellationToken
        );
        return resolved
            .FirstOrDefault(user => user.Login.Equals(login, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private static Result<PrivateDeliveryReceipt, PrivateDeliveryError> Success()
    {
        return Result<PrivateDeliveryReceipt, PrivateDeliveryError>.Success(
            new PrivateDeliveryReceipt()
        );
    }

    private static Result<PrivateDeliveryReceipt, PrivateDeliveryError> Error(
        PrivateDeliveryError error
    )
    {
        return Result<PrivateDeliveryReceipt, PrivateDeliveryError>.Error(error);
    }

    private sealed record WhisperHost(int Id);

    private sealed record PreparedPrivateDelivery(
        int HostId,
        string AccessToken,
        string SenderUserId,
        string RecipientUserId
    );

    private abstract record PrivateDeliveryPreparation
    {
        private PrivateDeliveryPreparation() { }

        internal sealed record Ready(PreparedPrivateDelivery Delivery) : PrivateDeliveryPreparation;

        internal sealed record Failed(PrivateDeliveryError Error) : PrivateDeliveryPreparation;
    }
}
