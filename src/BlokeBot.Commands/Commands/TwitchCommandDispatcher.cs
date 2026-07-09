using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Commands;

public sealed class TwitchCommandDispatcher
{
    private readonly object gate = new();
    private readonly TwitchCommandRegistry registry;
    private readonly IServiceProvider services;
    private TwitchCommandPlan? plan;

    internal TwitchCommandDispatcher(TwitchCommandRegistry registry, IServiceProvider services)
    {
        this.registry = registry;
        this.services = services;
    }

    public async ValueTask DispatchAsync(
        TwitchChatMessage message,
        Func<string, CancellationToken, ValueTask> reply,
        CancellationToken cancellationToken
    ) =>
        await DispatchResponsesAsync(
            message,
            (response, ct) => reply(response.Message, ct),
            cancellationToken
        );

    public async ValueTask DispatchResponsesAsync(
        TwitchChatMessage message,
        Func<TwitchCommandResponse, CancellationToken, ValueTask> respond,
        CancellationToken cancellationToken
    )
    {
        var commandPlan = GetPlan();
        if (!TryParseCommand(message.Text, out var route, out var args))
            return;

        var context = new TwitchCommandContext(message, route, services, respond, true);

        foreach (var filterType in commandPlan.Filters)
        {
            var filter =
                (ITwitchCommandFilter?)services.GetService(filterType)
                ?? (ITwitchCommandFilter)ActivatorUtilities.CreateInstance(services, filterType);
            if (!await filter.AllowAsync(context, cancellationToken))
                return;
        }

        var matched = commandPlan.Routes.TryGetValue(route, out var handler);
        if (matched && handler is not null)
        {
            await handler(context, args, cancellationToken);
            return;
        }

        foreach (var dynamicHandler in commandPlan.DynamicHandlers)
        {
            if (await dynamicHandler(context, args, cancellationToken))
                return;
        }

        if (commandPlan.FallbackHandler is not null)
            await commandPlan.FallbackHandler(context, args, cancellationToken);
    }

    private TwitchCommandPlan GetPlan()
    {
        if (plan is not null)
            return plan;

        lock (gate)
            return plan ??= registry.Build(services);
    }

    private static bool TryParseCommand(
        string text,
        out string route,
        out IReadOnlyList<string> args
    )
    {
        route = string.Empty;
        args = [];

        if (string.IsNullOrWhiteSpace(text) || text[0] != '!')
            return false;

        var parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (parts.Length == 0 || parts[0].Length <= 1)
            return false;

        route = parts[0][1..];
        args = parts[1..];
        return route.Length > 0;
    }
}
