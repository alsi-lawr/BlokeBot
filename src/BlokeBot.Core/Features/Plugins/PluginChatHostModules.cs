using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginChatHostModule(
    IPluginHostContextResolver hosts,
    IPublicChatMessageSender chat
) : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Chat;

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var host = await hosts.FindAsync(
            ((PluginInvocationContext.Channel)call.Context).Host,
            cancellationToken
        );
        if (host is null)
        {
            return Failed("Channel is unavailable.");
        }
        var outcome = await chat.SendAsync(
            host.Login,
            ((PluginValue.String)call.Arguments[0]).Value,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        return outcome.Match<PluginHostCallOutcome>(
            static _ => Returned(),
            static _ => Failed("Chat message was rejected.")
        );
    }

    internal static PluginHostCallOutcome.Returned Returned() => new(new PluginValue.Nil());

    internal static PluginHostCallOutcome.Failed Failed(string message) =>
        new(new(PluginHostFailureCode.ProviderRejected, message));
}

public sealed class PluginResponsesHostModule(
    IPluginHostContextResolver hosts,
    IPublicChatMessageSender chat,
    WhisperCommandResponseSender whispers
) : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Responses;

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var context = (PluginInvocationContext.Channel)call.Context;
        var host = await hosts.FindAsync(context.Host, cancellationToken);
        if (host is null)
        {
            return PluginChatHostModule.Failed("Channel is unavailable.");
        }
        var message = ((PluginValue.String)call.Arguments[0]).Value;
        if (call.Operation == PluginStandardHostModules.Responses.Operations[0].Id)
        {
            var outcome = await chat.SendAsync(
                host.Login,
                message,
                new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                cancellationToken
            );
            return outcome.Match<PluginHostCallOutcome>(
                static _ => PluginChatHostModule.Returned(),
                static _ => PluginChatHostModule.Failed("Chat response was rejected.")
            );
        }
        if (context.Actor is null)
        {
            return PluginChatHostModule.Failed("Whisper response requires an actor.");
        }
        var source = new ChatMessage(
            context.Actor.Login,
            host.Login,
            string.Empty,
            string.Empty,
            new Dictionary<string, string>()
        );
        var delivered = await whispers.Deliver(source, message).ExecuteAsync(cancellationToken);
        return delivered.Match<PluginHostCallOutcome>(
            static _ => PluginChatHostModule.Returned(),
            static _ => PluginChatHostModule.Failed("Whisper response was rejected.")
        );
    }
}
