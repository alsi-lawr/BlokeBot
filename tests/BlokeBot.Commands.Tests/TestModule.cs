using BlokeBot.Commands;

namespace BlokeBot.Commands.Tests;

internal sealed class TestModule : ITwitchCommandModule
{
    public void AddCommands(ITwitchCommandBuilder commands)
    {
        commands.Map("module", async (ctx, args, ct) => await ctx.ReplyAsync(args[0], ct));
    }
}
