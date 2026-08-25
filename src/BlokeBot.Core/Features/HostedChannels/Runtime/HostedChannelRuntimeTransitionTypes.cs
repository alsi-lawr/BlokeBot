namespace BlokeBot.Core.Features.HostedChannels.Runtime;

internal enum HostedChannelAccountSelectionRuntimeChange
{
    None,
    Restart,
    Stop,
}

internal enum PendingAccountRuntimeChange
{
    None,
    Restart,
    Stop,
    ForceStop,
}

internal enum HostedChannelRuntimeTransitionOutcome
{
    HostNotFound,
    Unchanged,
    Transitioned,
}
