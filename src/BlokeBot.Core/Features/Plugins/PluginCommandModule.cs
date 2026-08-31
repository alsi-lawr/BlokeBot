using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal sealed class PluginCommandModule(
    IPluginHostContextResolver hosts,
    IPluginDispatchSnapshotProvider dispatch,
    IPluginDispatchInvoker invoker,
    IPluginAutomationSourceAdmission? automationSources = null
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands) => commands.MapDynamic(ExecuteAsync);

    private async ValueTask<CommandHandlingOutcome> ExecuteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var host = await hosts.FindAsync(context.Message.Channel, cancellationToken);
        if (host is null)
        {
            return new CommandHandlingOutcome.Unhandled();
        }
        var route = CommandAliasNormalizer.Normalize(context.CommandName);
        if (!dispatch.Current.Commands.TryGetValue(new(host.Id, route), out var endpoint))
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        var actor = Actor(context.Message);
        var invocationContext = new PluginInvocationContext.Channel(
            endpoint.Declaration.Installation,
            host.Id,
            actor,
            Command: new(route, args)
        );
        var outcome = await invoker.InvokeCommandAsync(
            endpoint,
            invocationContext,
            PluginInvocationInputs.Command(route, args),
            cancellationToken
        );
        if (
            outcome is PluginDispatchInvocationOutcome.Returned returned
            && !returned.AutomationSources.IsDefaultOrEmpty
            && automationSources is not null
        )
        {
            await automationSources.AdmitAsync(
                endpoint,
                invocationContext,
                returned.AutomationSources,
                cancellationToken
            );
        }
        return outcome is PluginDispatchInvocationOutcome.Rejected
            ? new CommandHandlingOutcome.Unhandled()
            : new CommandHandlingOutcome.Handled();
    }

    private static PluginActorContext Actor(ChatMessage message)
    {
        _ = message.Tags.TryGetValue("display-name", out var displayName);
        _ = message.Tags.TryGetValue("user-id", out var userId);
        _ = message.Tags.TryGetValue("badges", out var badges);
        _ = message.Tags.TryGetValue("mod", out var moderator);
        _ = message.Tags.TryGetValue("subscriber", out var subscriber);
        return new(
            message.Login,
            string.IsNullOrWhiteSpace(displayName) ? message.Login : displayName,
            userId,
            string.Equals(message.Login, message.Channel, StringComparison.OrdinalIgnoreCase),
            moderator == "1" || (badges?.Contains("moderator/", StringComparison.Ordinal) ?? false),
            subscriber == "1"
                || (badges?.Contains("subscriber/", StringComparison.Ordinal) ?? false)
        );
    }
}
