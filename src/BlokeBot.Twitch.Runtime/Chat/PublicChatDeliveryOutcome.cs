using System.Net;

namespace BlokeBot.Twitch.Runtime;

internal readonly record struct PublicChatFailureType(string Value)
{
    internal static PublicChatFailureType From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(exception.GetType().FullName ?? exception.GetType().Name);
    }
}

internal readonly record struct PublicChatHttpStatusCode(int Value)
{
    internal static PublicChatHttpStatusCode From(HttpStatusCode statusCode) =>
        new((int)statusCode);
}

internal readonly record struct PublicChatProviderRejectionCode(string Value);

internal sealed record PublicChatSendLeaseExpired;

internal sealed record PublicChatUnclassifiedPostBoundaryFailure;

internal abstract record PublicChatHttpStatus
{
    private PublicChatHttpStatus() { }

    internal abstract TResult Match<TResult>(
        Func<PublicChatHttpStatusCode, TResult> known,
        Func<TResult> unavailable
    );

    internal sealed record Known(PublicChatHttpStatusCode Code) : PublicChatHttpStatus
    {
        internal override TResult Match<TResult>(
            Func<PublicChatHttpStatusCode, TResult> known,
            Func<TResult> unavailable
        ) => known(Code);
    }

    internal sealed record Unavailable : PublicChatHttpStatus
    {
        internal override TResult Match<TResult>(
            Func<PublicChatHttpStatusCode, TResult> known,
            Func<TResult> unavailable
        ) => unavailable();
    }
}

internal abstract record PublicChatFailureDiagnostic
{
    private PublicChatFailureDiagnostic() { }

    internal required PublicChatFailureType FailureType { get; init; }

    internal required PublicChatHttpStatus HttpStatus { get; init; }

    internal abstract TResult Match<TResult>(
        Func<Preparation, TResult> preparation,
        Func<Send, TResult> send
    );

    internal sealed record Preparation : PublicChatFailureDiagnostic
    {
        internal override TResult Match<TResult>(
            Func<Preparation, TResult> preparation,
            Func<Send, TResult> send
        ) => preparation(this);
    }

    internal sealed record Send : PublicChatFailureDiagnostic
    {
        internal override TResult Match<TResult>(
            Func<Preparation, TResult> preparation,
            Func<Send, TResult> send
        ) => send(this);
    }
}

internal abstract record PublicChatRejectionReason
{
    private PublicChatRejectionReason() { }

    internal abstract TResult Match<TResult>(
        Func<PublicChatProviderRejectionCode, TResult> providerCode,
        Func<TResult> unspecified
    );

    internal sealed record ProviderCode(PublicChatProviderRejectionCode Code)
        : PublicChatRejectionReason
    {
        internal override TResult Match<TResult>(
            Func<PublicChatProviderRejectionCode, TResult> providerCode,
            Func<TResult> unspecified
        ) => providerCode(Code);
    }

    internal sealed record Unspecified : PublicChatRejectionReason
    {
        internal override TResult Match<TResult>(
            Func<PublicChatProviderRejectionCode, TResult> providerCode,
            Func<TResult> unspecified
        ) => unspecified();
    }
}

internal abstract record PublicChatDeliveryOutcome
{
    private PublicChatDeliveryOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Sent, TResult> sent,
        Func<MissingChannel, TResult> missingChannel,
        Func<MissingBot, TResult> missingBot,
        Func<TokenUnavailable, TResult> tokenUnavailable,
        Func<SafePreSendTransient, TResult> safePreSendTransient,
        Func<Rejection, TResult> rejection,
        Func<Ambiguous, TResult> ambiguous,
        Func<Unexpected, TResult> unexpected
    );

    internal abstract void Match(
        Action<Sent> sent,
        Action<MissingChannel> missingChannel,
        Action<MissingBot> missingBot,
        Action<TokenUnavailable> tokenUnavailable,
        Action<SafePreSendTransient> safePreSendTransient,
        Action<Rejection> rejection,
        Action<Ambiguous> ambiguous,
        Action<Unexpected> unexpected
    );

    internal sealed record Sent(string TwitchMessageId = "test-message-id")
        : PublicChatDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        ) => sent(this);

        internal override void Match(
            Action<Sent> sent,
            Action<MissingChannel> missingChannel,
            Action<MissingBot> missingBot,
            Action<TokenUnavailable> tokenUnavailable,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        ) => sent(this);
    }

    internal sealed record MissingChannel : PublicChatDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        ) => missingChannel(this);

        internal override void Match(
            Action<Sent> sent,
            Action<MissingChannel> missingChannel,
            Action<MissingBot> missingBot,
            Action<TokenUnavailable> tokenUnavailable,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        ) => missingChannel(this);
    }

    internal sealed record MissingBot : PublicChatDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        ) => missingBot(this);

        internal override void Match(
            Action<Sent> sent,
            Action<MissingChannel> missingChannel,
            Action<MissingBot> missingBot,
            Action<TokenUnavailable> tokenUnavailable,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        ) => missingBot(this);
    }

    internal sealed record TokenUnavailable(AccessTokenUnavailableReason Reason)
        : PublicChatDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        ) => tokenUnavailable(this);

        internal override void Match(
            Action<Sent> sent,
            Action<MissingChannel> missingChannel,
            Action<MissingBot> missingBot,
            Action<TokenUnavailable> tokenUnavailable,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        ) => tokenUnavailable(this);
    }

    internal sealed record SafePreSendTransient : PublicChatDeliveryOutcome
    {
        internal required PublicChatFailureDiagnostic.Preparation Diagnostic { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        ) => safePreSendTransient(this);

        internal override void Match(
            Action<Sent> sent,
            Action<MissingChannel> missingChannel,
            Action<MissingBot> missingBot,
            Action<TokenUnavailable> tokenUnavailable,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        ) => safePreSendTransient(this);
    }

    internal sealed record Rejection : PublicChatDeliveryOutcome
    {
        internal required PublicChatRejectionReason Reason { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        ) => rejection(this);

        internal override void Match(
            Action<Sent> sent,
            Action<MissingChannel> missingChannel,
            Action<MissingBot> missingBot,
            Action<TokenUnavailable> tokenUnavailable,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        ) => rejection(this);
    }

    internal sealed record Ambiguous : PublicChatDeliveryOutcome
    {
        internal required PublicChatFailureDiagnostic.Send Diagnostic { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        ) => ambiguous(this);

        internal override void Match(
            Action<Sent> sent,
            Action<MissingChannel> missingChannel,
            Action<MissingBot> missingBot,
            Action<TokenUnavailable> tokenUnavailable,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        ) => ambiguous(this);
    }

    internal sealed record Unexpected : PublicChatDeliveryOutcome
    {
        internal required PublicChatFailureDiagnostic.Preparation Diagnostic { get; init; }

        internal required Exception Cause { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        ) => unexpected(this);

        internal override void Match(
            Action<Sent> sent,
            Action<MissingChannel> missingChannel,
            Action<MissingBot> missingBot,
            Action<TokenUnavailable> tokenUnavailable,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        ) => unexpected(this);

        public override string ToString() =>
            $"{nameof(Unexpected)} {{ Diagnostic = {Diagnostic} }}";
    }
}

internal sealed record PublicChatPreparedSend
{
    internal required PublicChatClaimedMessage Message { get; init; }

    internal required string AppAccessToken { get; init; }

    internal required string BroadcasterId { get; init; }

    internal required string BotUserId { get; init; }

    public override string ToString() =>
        $"{nameof(PublicChatPreparedSend)} {{ OutboxMessageId = {Message.Id} }}";
}

internal abstract record PublicChatPreparationOutcome
{
    private PublicChatPreparationOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Ready, TResult> ready,
        Func<MissingChannel, TResult> missingChannel,
        Func<MissingBot, TResult> missingBot,
        Func<TokenUnavailable, TResult> tokenUnavailable,
        Func<SafePreSendTransient, TResult> safePreSendTransient,
        Func<Unexpected, TResult> unexpected
    );

    internal sealed record Ready : PublicChatPreparationOutcome
    {
        internal required PublicChatPreparedSend Send { get; init; }

        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        ) => ready(this);
    }

    internal sealed record MissingChannel : PublicChatPreparationOutcome
    {
        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        ) => missingChannel(this);
    }

    internal sealed record MissingBot : PublicChatPreparationOutcome
    {
        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        ) => missingBot(this);
    }

    internal sealed record TokenUnavailable(AccessTokenUnavailableReason Reason)
        : PublicChatPreparationOutcome
    {
        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        ) => tokenUnavailable(this);
    }

    internal sealed record SafePreSendTransient : PublicChatPreparationOutcome
    {
        internal required PublicChatFailureDiagnostic.Preparation Diagnostic { get; init; }

        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        ) => safePreSendTransient(this);
    }

    internal sealed record Unexpected : PublicChatPreparationOutcome
    {
        internal required PublicChatFailureDiagnostic.Preparation Diagnostic { get; init; }

        internal required Exception Cause { get; init; }

        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        ) => unexpected(this);

        public override string ToString() =>
            $"{nameof(Unexpected)} {{ Diagnostic = {Diagnostic} }}";
    }
}

internal abstract record PublicChatTransportSendResult
{
    private PublicChatTransportSendResult() { }

    internal abstract TResult Match<TResult>(
        Func<Sent, TResult> sent,
        Func<Rejected, TResult> rejected
    );

    internal abstract void Match(Action<Sent> sent, Action<Rejected> rejected);

    internal sealed record Sent(string TwitchMessageId = "test-message-id")
        : PublicChatTransportSendResult
    {
        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<Rejected, TResult> rejected
        ) => sent(this);

        internal override void Match(Action<Sent> sent, Action<Rejected> rejected) => sent(this);
    }

    internal sealed record Rejected : PublicChatTransportSendResult
    {
        internal required PublicChatRejectionReason Reason { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<Rejected, TResult> rejected
        ) => rejected(this);

        internal override void Match(Action<Sent> sent, Action<Rejected> rejected) =>
            rejected(this);
    }
}
