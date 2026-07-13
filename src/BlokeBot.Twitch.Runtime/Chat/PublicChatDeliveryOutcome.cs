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
    internal static PublicChatHttpStatusCode From(HttpStatusCode statusCode)
    {
        return new((int)statusCode);
    }
}

internal readonly record struct PublicChatProviderRejectionCode(string Value);

internal sealed record PublicChatSendLeaseExpired;

internal sealed record PublicChatUnclassifiedPostBoundaryFailure;

internal abstract record PublicChatHttpStatus
{
    private protected PublicChatHttpStatus() { }

    internal abstract TResult Match<TResult>(
        Func<PublicChatHttpStatusCode, TResult> known,
        Func<TResult> unavailable
    );

    private protected abstract void Seal();

    internal sealed record Known(PublicChatHttpStatusCode Code) : PublicChatHttpStatus
    {
        internal override TResult Match<TResult>(
            Func<PublicChatHttpStatusCode, TResult> known,
            Func<TResult> unavailable
        )
        {
            return known(Code);
        }

        private protected override void Seal() { }
    }

    internal sealed record Unavailable : PublicChatHttpStatus
    {
        internal override TResult Match<TResult>(
            Func<PublicChatHttpStatusCode, TResult> known,
            Func<TResult> unavailable
        )
        {
            return unavailable();
        }

        private protected override void Seal() { }
    }
}

internal abstract record PublicChatFailureDiagnostic
{
    private protected PublicChatFailureDiagnostic() { }

    internal required PublicChatFailureType FailureType { get; init; }

    internal required PublicChatHttpStatus HttpStatus { get; init; }

    internal abstract TResult Match<TResult>(
        Func<Preparation, TResult> preparation,
        Func<Send, TResult> send
    );

    private protected abstract void Seal();

    internal sealed record Preparation : PublicChatFailureDiagnostic
    {
        internal override TResult Match<TResult>(
            Func<Preparation, TResult> preparation,
            Func<Send, TResult> send
        )
        {
            return preparation(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record Send : PublicChatFailureDiagnostic
    {
        internal override TResult Match<TResult>(
            Func<Preparation, TResult> preparation,
            Func<Send, TResult> send
        )
        {
            return send(this);
        }

        private protected override void Seal() { }
    }
}

internal abstract record PublicChatRejectionReason
{
    private protected PublicChatRejectionReason() { }

    internal abstract TResult Match<TResult>(
        Func<PublicChatProviderRejectionCode, TResult> providerCode,
        Func<TResult> unspecified
    );

    private protected abstract void Seal();

    internal sealed record ProviderCode(PublicChatProviderRejectionCode Code)
        : PublicChatRejectionReason
    {
        internal override TResult Match<TResult>(
            Func<PublicChatProviderRejectionCode, TResult> providerCode,
            Func<TResult> unspecified
        )
        {
            return providerCode(Code);
        }

        private protected override void Seal() { }
    }

    internal sealed record Unspecified : PublicChatRejectionReason
    {
        internal override TResult Match<TResult>(
            Func<PublicChatProviderRejectionCode, TResult> providerCode,
            Func<TResult> unspecified
        )
        {
            return unspecified();
        }

        private protected override void Seal() { }
    }
}

internal abstract record PublicChatDeliveryOutcome
{
    private protected PublicChatDeliveryOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Sent, TResult> sent,
        Func<SafePreSendTransient, TResult> safePreSendTransient,
        Func<Rejection, TResult> rejection,
        Func<Ambiguous, TResult> ambiguous,
        Func<Unexpected, TResult> unexpected
    );

    internal abstract void Match(
        Action<Sent> sent,
        Action<SafePreSendTransient> safePreSendTransient,
        Action<Rejection> rejection,
        Action<Ambiguous> ambiguous,
        Action<Unexpected> unexpected
    );

    private protected abstract void Seal();

    internal sealed record Sent : PublicChatDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        )
        {
            return sent(this);
        }

        internal override void Match(
            Action<Sent> sent,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        )
        {
            sent(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record SafePreSendTransient : PublicChatDeliveryOutcome
    {
        internal required PublicChatFailureDiagnostic.Preparation Diagnostic
        {
            get;
            init;
        }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        )
        {
            return safePreSendTransient(this);
        }

        internal override void Match(
            Action<Sent> sent,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        )
        {
            safePreSendTransient(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record Rejection : PublicChatDeliveryOutcome
    {
        internal required PublicChatRejectionReason Reason { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        )
        {
            return rejection(this);
        }

        internal override void Match(
            Action<Sent> sent,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        )
        {
            rejection(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record Ambiguous : PublicChatDeliveryOutcome
    {
        internal required PublicChatFailureDiagnostic.Send Diagnostic { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        )
        {
            return ambiguous(this);
        }

        internal override void Match(
            Action<Sent> sent,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        )
        {
            ambiguous(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record Unexpected : PublicChatDeliveryOutcome
    {
        internal required PublicChatFailureDiagnostic.Preparation Diagnostic
        {
            get;
            init;
        }

        internal required Exception Cause { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Rejection, TResult> rejection,
            Func<Ambiguous, TResult> ambiguous,
            Func<Unexpected, TResult> unexpected
        )
        {
            return unexpected(this);
        }

        internal override void Match(
            Action<Sent> sent,
            Action<SafePreSendTransient> safePreSendTransient,
            Action<Rejection> rejection,
            Action<Ambiguous> ambiguous,
            Action<Unexpected> unexpected
        )
        {
            unexpected(this);
        }

        private protected override void Seal() { }

        public override string ToString()
        {
            return $"{nameof(Unexpected)} {{ Diagnostic = {Diagnostic} }}";
        }
    }
}

internal sealed record PublicChatPreparedSend
{
    internal required PublicChatClaimedMessage Message { get; init; }

    internal required string AppAccessToken { get; init; }

    internal required string BroadcasterId { get; init; }

    internal required string BotUserId { get; init; }

    public override string ToString()
    {
        return $"{nameof(PublicChatPreparedSend)} {{ OutboxMessageId = {Message.Id} }}";
    }
}

internal abstract record PublicChatPreparationOutcome
{
    private protected PublicChatPreparationOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Ready, TResult> ready,
        Func<SafePreSendTransient, TResult> safePreSendTransient,
        Func<Unexpected, TResult> unexpected
    );

    private protected abstract void Seal();

    internal sealed record Ready : PublicChatPreparationOutcome
    {
        internal required PublicChatPreparedSend Send { get; init; }

        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        )
        {
            return ready(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record SafePreSendTransient : PublicChatPreparationOutcome
    {
        internal required PublicChatFailureDiagnostic.Preparation Diagnostic
        {
            get;
            init;
        }

        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        )
        {
            return safePreSendTransient(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record Unexpected : PublicChatPreparationOutcome
    {
        internal required PublicChatFailureDiagnostic.Preparation Diagnostic
        {
            get;
            init;
        }

        internal required Exception Cause { get; init; }

        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<SafePreSendTransient, TResult> safePreSendTransient,
            Func<Unexpected, TResult> unexpected
        )
        {
            return unexpected(this);
        }

        private protected override void Seal() { }

        public override string ToString()
        {
            return $"{nameof(Unexpected)} {{ Diagnostic = {Diagnostic} }}";
        }
    }
}

internal abstract record PublicChatTransportSendResult
{
    private protected PublicChatTransportSendResult() { }

    internal abstract TResult Match<TResult>(
        Func<Sent, TResult> sent,
        Func<Rejected, TResult> rejected
    );

    internal abstract void Match(
        Action<Sent> sent,
        Action<Rejected> rejected
    );

    private protected abstract void Seal();

    internal sealed record Sent : PublicChatTransportSendResult
    {
        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<Rejected, TResult> rejected
        )
        {
            return sent(this);
        }

        internal override void Match(
            Action<Sent> sent,
            Action<Rejected> rejected
        )
        {
            sent(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record Rejected : PublicChatTransportSendResult
    {
        internal required PublicChatRejectionReason Reason { get; init; }

        internal override TResult Match<TResult>(
            Func<Sent, TResult> sent,
            Func<Rejected, TResult> rejected
        )
        {
            return rejected(this);
        }

        internal override void Match(
            Action<Sent> sent,
            Action<Rejected> rejected
        )
        {
            rejected(this);
        }

        private protected override void Seal() { }
    }
}
