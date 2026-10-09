namespace BlokeBot.Persistence.Models;

public enum CustomValueScope
{
    Global,
    User,
}

public enum CustomValueKind
{
    Number,
    Text,
    Dictionary,
}

public sealed class CustomValueDefinition
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameHash { get; set; } = string.Empty;
    public CustomValueScope Scope { get; set; }
    public CustomValueKind Kind { get; set; }
    public long DefaultNumber { get; set; }
    public string DefaultText { get; set; } = string.Empty;
    public Guid Revision { get; set; }
}

public sealed class CustomStoredValue
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public int DefinitionId { get; set; }
    public string ViewerId { get; set; } = string.Empty;
    public string EntryKey { get; set; } = string.Empty;
    public string TargetHash { get; set; } = string.Empty;
    public CustomValueKind Kind { get; set; }
    public long Number { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public Guid Revision { get; set; }
}

public sealed class CustomCommandComputedResult
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public int CommandId { get; set; }
    public string InvocationId { get; set; } = string.Empty;
    public string InvocationHash { get; set; } = string.Empty;
    public string ViewerId { get; set; } = string.Empty;
    public string Reply { get; set; } = string.Empty;
    public bool ReplyEligible { get; set; } = true;
}
