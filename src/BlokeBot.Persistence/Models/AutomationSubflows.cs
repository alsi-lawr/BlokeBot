namespace BlokeBot.Persistence.Models;

public sealed class AutomationSubflow
{
    public int HostId { get; set; }
    public Guid Id { get; set; }
    public int LastRevision { get; set; }
}

public sealed class AutomationSubflowRevisionRecord
{
    public int HostId { get; set; }
    public Guid Id { get; set; }
    public Guid SubflowId { get; set; }
    public int Revision { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
}

public sealed class AutomationSubflowCallerReference
{
    public Guid NodeId { get; set; }
    public int HostId { get; set; }
    public Guid RevisionId { get; set; }
}

public sealed class AutomationSubflowRevisionReference
{
    public int HostId { get; set; }
    public Guid CallerRevisionId { get; set; }
    public Guid NodeId { get; set; }
    public Guid RevisionId { get; set; }
}

public sealed class AutomationSubflowRunReference
{
    public Guid RunId { get; set; }
    public int HostId { get; set; }
    public Guid RevisionId { get; set; }
}
