namespace BlokeBot.Plugins.Contracts;

public static class PluginWorkerLimits
{
    public const int ProtocolVersion = 1;
    public const int MaximumFrameBytes = 1024 * 1024;
    public const int MaximumConcurrentInvocations = 1;
    public const int MaximumConcurrentHostCalls = 1;
    public const int MaximumQueuedMessages = 32;
    public const int MaximumDiagnosticsPerInvocation = 128;
    public const int MaximumDiagnosticBytes = 64 * 1024;
    public const int MaximumDiagnosticLineBytes = 2 * 1024;
    public const int MaximumProcessOutputBytes = 64 * 1024;
    public const int MaximumInvocationDurationMilliseconds = 30_000;
    public const int HandshakeTimeoutMilliseconds = 5_000;
    public const int CancellationGraceMilliseconds = 750;
}
