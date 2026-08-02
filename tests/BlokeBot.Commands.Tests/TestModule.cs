namespace BlokeBot.Commands.Tests;

internal sealed class TestModule : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands) =>
        commands.Map("module", async (ctx, args, ct) => await ctx.ReplyAsync(args[0], ct));
}
