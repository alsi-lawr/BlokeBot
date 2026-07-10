namespace BlokeBot.Persistence.Models;

public abstract class CustomCommandAction
{
    public int CustomCommandId { get; set; }

    public int HostId { get; set; }

    public int MessageLibraryEntryId { get; set; }

    public CustomCommand? Command { get; set; }

    public CustomMessageLibraryEntry? MessageLibraryEntry { get; set; }
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
