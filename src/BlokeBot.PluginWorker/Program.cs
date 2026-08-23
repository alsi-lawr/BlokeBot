using System.IO.Pipes;
using BlokeBot.Plugins.Runtime;
using BlokeBot.PluginWorker;

if (args is ["--deployment-probe", var probeStateRoot])
{
    return await PluginWorkerDeploymentProbe.RunAsync(probeStateRoot);
}

var parsed = PluginWorkerLaunchArgumentParser.Parse(args);
if (
    parsed is not PluginWorkerLaunchArgumentOutcome.Accepted accepted
    || !PluginRuntimeIdentifierResolver.TryResolveCurrent(out var runtimeIdentifier)
)
{
    return 64;
}

await using var pipe = new NamedPipeClientStream(
    ".",
    accepted.Arguments.PipeName,
    PipeDirection.InOut,
    PipeOptions.Asynchronous
);
using var connectionCancellation = new CancellationTokenSource(
    TimeSpan.FromMilliseconds(
        BlokeBot.Plugins.Contracts.PluginWorkerLimits.HandshakeTimeoutMilliseconds
    )
);
await pipe.ConnectAsync(connectionCancellation.Token);
await using var session = new PluginWorkerSession(pipe, accepted.Arguments, runtimeIdentifier);
return await session.RunAsync(CancellationToken.None);
