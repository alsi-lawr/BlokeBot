namespace BlokeBot.Commands.Tests;

public sealed class ChatCommandCompositionTests
{
    private static ChatCommandRegistration Registration(Action<IChatCommandBuilder> configure) =>
        new() { Configure = configure };

    private sealed class CompositionModule : IChatCommandModule
    {
        public void AddCommands(IChatCommandBuilder commands) =>
            commands.Map("module", static (_, _, _) => ValueTask.CompletedTask);
    }

    private sealed class AllowFilter : IChatCommandFilter;
}
