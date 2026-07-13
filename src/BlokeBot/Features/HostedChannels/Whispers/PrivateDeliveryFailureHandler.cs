using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.HostedChannels.Whispers;

public sealed record PrivateDeliveryFailureContext
{
    public required string HostChannel { get; init; }
}

public interface IPrivateDeliveryFailureHandler
{
    ValueTask HandleAsync(
        PrivateDeliveryError error,
        PrivateDeliveryFailureContext context,
        CancellationToken cancellationToken
    );
}

public sealed class PrivateDeliveryFailureTelemetryHandler(
    ILogger<PrivateDeliveryFailureTelemetryHandler> log
) : IPrivateDeliveryFailureHandler
{
    public ValueTask HandleAsync(
        PrivateDeliveryError error,
        PrivateDeliveryFailureContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        log.LogWarning(
            "Private command response delivery failed for host channel #{HostChannel} with classification {Classification}; no additional user-visible delivery was attempted.",
            context.HostChannel,
            error.GetType().Name
        );
        return ValueTask.CompletedTask;
    }
}

public sealed class PrivateDeliveryFailureHandlingException : Exception
{
    internal PrivateDeliveryFailureHandlingException(
        PrivateDeliveryError deliveryError,
        PrivateDeliveryFailureContext context,
        Exception innerException
    )
        : base("Private delivery failure telemetry handling failed.", innerException)
    {
        DeliveryError = deliveryError;
        Context = context;
    }

    internal PrivateDeliveryError DeliveryError { get; }

    internal PrivateDeliveryFailureContext Context { get; }
}
