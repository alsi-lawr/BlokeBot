namespace BlokeBot.Persistence.Models;

public abstract class CustomCommandAction
{
    public int CustomCommandId { get; set; }

    public int HostId { get; set; }

    public int? ZeroArgumentMessageLibraryEntryId { get; set; }

    public int? OneArgumentMessageLibraryEntryId { get; set; }

    public int? TwoArgumentMessageLibraryEntryId { get; set; }

    public CustomCommand? Command { get; set; }

    public CustomMessageLibraryEntry? ZeroArgumentMessageLibraryEntry { get; set; }

    public CustomMessageLibraryEntry? OneArgumentMessageLibraryEntry { get; set; }

    public CustomMessageLibraryEntry? TwoArgumentMessageLibraryEntry { get; set; }

    public int? ReplyIdForArgumentCount(int argumentCount)
    {
        return argumentCount switch
        {
            0 => ZeroArgumentMessageLibraryEntryId,
            1 => OneArgumentMessageLibraryEntryId,
            2 => TwoArgumentMessageLibraryEntryId,
            _ => null,
        };
    }
}

public sealed class MessageCustomCommandAction : CustomCommandAction
{
    public const string Discriminator = "Message";
}

public sealed class CounterCustomCommandAction : CustomCommandAction
{
    public const string Discriminator = "Counter";

    public int CounterId { get; set; }

    public CustomCounter? Counter { get; set; }
}

public enum OverlayCueReplyOrder
{
    [PersistedToken("before")]
    Before,

    [PersistedToken("after")]
    After,
}

public sealed class OverlayCueCustomCommandAction : CustomCommandAction
{
    public const string Discriminator = "OverlayCue";

    public Guid TargetOverlayPublicId { get; set; }

    public Guid CuePublicId { get; set; }

    public OverlayCueQueuePolicy QueuePolicy { get; set; }

    public OverlayCueReplyOrder ReplyOrder { get; set; }
}
