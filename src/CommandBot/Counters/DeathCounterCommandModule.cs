using BlokeBot.Commands;
using BlokeBot.Twitch.Runtime;

public sealed class DeathCounterCommandModule(ICounterStore store) : ITwitchCommandModule
{
    public void AddCommands(ITwitchCommandBuilder commands)
    {
        commands
            .Map(
                "deaths",
                async (ctx, args, ct) =>
                {
                    if (args.Count != 1 || !int.TryParse(args[0], out var value) || value < 0)
                    {
                        await ctx.ReplyAsync("Usage: !deaths <deaths>", ct);
                        return;
                    }

                    await store.SaveAsync(CounterKeys.Deaths, value, ct);
                    await ctx.ReplyAsync($"Oh no, I've died {value} times", ct);
                }
            )
            .Map(
                "deathsi",
                async (ctx, _, ct) =>
                {
                    var deaths = await store.LoadAsync(CounterKeys.Deaths, ct) + 1;
                    await store.SaveAsync(CounterKeys.Deaths, deaths, ct);
                    await ctx.ReplyAsync($"Oh no, I've died {deaths} times", ct);
                }
            );
    }
}
