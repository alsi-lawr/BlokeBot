using Alsi.TwitchBot;

namespace BlokeBot.BotStatus;

internal sealed class OfflineBotStatusAccessor : ITwitchBotRuntimeStatusAccessor
{
    public event Action? Changed
    {
        add { }
        remove { }
    }

    public TwitchBotRuntimeStatus Current { get; } = new(false, false, []);
}
