namespace BlokeBot.Commands;

public sealed class ChatCommandDispatcher
{
    private readonly ChatCommandPlan _plan;

    internal ChatCommandDispatcher(ChatCommandRegistry registry) => _plan = registry.Plan;

    public async ValueTask DispatchResponsesAsync(
        ChatMessage message,
        CommandResponder respond,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseCommand(message.Text, out var route, out var args))
        {
            return;
        }

        var context = new ChatCommandContext
        {
            Message = message,
            CommandName = route,
            Responder = respond,
        };

        foreach (var filter in _plan.Filters)
        {
            if (!await filter.AllowAsync(context, cancellationToken))
            {
                return;
            }
        }

        var matched = _plan.Routes.TryGetValue(route, out var handler);
        if (matched && handler is not null)
        {
            await handler(context, args, cancellationToken);
            return;
        }

        foreach (var dynamicHandler in _plan.DynamicHandlers)
        {
            var handled = await dynamicHandler(context, args, cancellationToken);
            if (handled.Match(_ => false, _ => true))
            {
                return;
            }
        }

        if (_plan.FallbackHandler is not null)
        {
            await _plan.FallbackHandler(context, args, cancellationToken);
        }
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
        {
            return false;
        }

        var parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (parts.Length == 0 || parts[0].Length <= 1)
        {
            return false;
        }

        route = parts[0][1..];
        args = parts[1..];
        return route.Length > 0;
    }
}
