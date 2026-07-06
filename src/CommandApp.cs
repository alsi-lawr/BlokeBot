using System.Collections.Concurrent;

public delegate ValueTask CommandHandler(CommandContext ctx, ReadOnlyMemory<string> args);
public delegate Task ChatSender(string message);

public readonly record struct CommandContext(string Login, string RawMessage, ChatSender Say);

public interface ICommandFilter
{
    bool Allow(CommandContext ctx);
}

public sealed class CommandApp
{
    private readonly List<ICommandFilter> filters = new();
    private readonly ConcurrentDictionary<string, CommandHandler> routes = new(
        StringComparer.OrdinalIgnoreCase
    );

    public CommandApp Use(ICommandFilter filter)
    {
        filters.Add(filter);
        return this;
    }

    public CommandApp Map(string route, CommandHandler handler)
    {
        routes[route] = handler;
        return this;
    }

    public ValueTask Dispatch(CommandContext ctx)
    {
        for (var i = 0; i < filters.Count; i++)
            if (!filters[i].Allow(ctx))
                return ValueTask.CompletedTask;

        if (!TryParseBang(ctx.RawMessage, out var route, out var args))
            return ValueTask.CompletedTask;

        return routes.TryGetValue(route, out var handler)
            ? handler(ctx, args)
            : ValueTask.CompletedTask;
    }

    private static bool TryParseBang(string raw, out string route, out ReadOnlyMemory<string> args)
    {
        route = string.Empty;
        args = default;

        if (string.IsNullOrWhiteSpace(raw) || raw[0] != '!')
            return false;

        var parts = raw.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (parts.Length == 0)
            return false;

        route = parts[0].Length > 1 ? parts[0][1..] : "";
        args = parts.AsMemory(1);
        return route.Length > 0;
    }
}

public sealed class AllowedUsersFilter(HashSet<string> allowed) : ICommandFilter
{
    public bool Allow(CommandContext ctx) => allowed.Contains(ctx.Login);
}
