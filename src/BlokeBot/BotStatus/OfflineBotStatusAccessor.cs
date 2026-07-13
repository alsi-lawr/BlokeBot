namespace BlokeBot.BotStatus;

internal sealed class OfflineBotStatusAccessor : IBotRuntimeStatusAccessor
{
    public event Action? Changed
    {
        add { }
        remove { }
    }

    public BotRuntimeStatus Current { get; } = new(false, false, []);
}
