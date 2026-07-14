namespace BlokeBot.Components;

public readonly record struct UiFaultContext(
    string Component,
    string Operation,
    int? HostId,
    string? LoadIdentityType
);

public sealed class UiFaultTelemetry(ILogger<UiFaultTelemetry> log)
{
    private static readonly EventId _unexpectedUiFault = new(7003, "UnexpectedUiFault");

    public void Report(Exception exception, UiFaultContext context)
    {
        log.LogError(
            _unexpectedUiFault,
            exception,
            "Unexpected UI fault in {UiComponent} during {UiOperation} for host {HostId} with load identity {LoadIdentityType}",
            context.Component,
            context.Operation,
            context.HostId,
            context.LoadIdentityType
        );
    }
}
