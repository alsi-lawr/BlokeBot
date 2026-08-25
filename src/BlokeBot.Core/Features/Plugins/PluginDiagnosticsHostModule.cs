using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginDiagnosticsHostModule(ILogger<PluginDiagnosticsHostModule> logger)
    : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Diagnostics;

    public ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var level = ((PluginValue.String)call.Arguments[0]).Value;
        var message = ((PluginValue.String)call.Arguments[1]).Value;
        switch (level)
        {
            case "trace":
                logger.LogTrace("Plugin diagnostic: {Message}", message);
                break;
            case "warning":
                logger.LogWarning("Plugin diagnostic: {Message}", message);
                break;
            case "error":
                logger.LogError("Plugin diagnostic: {Message}", message);
                break;
            default:
                logger.LogInformation("Plugin diagnostic: {Message}", message);
                break;
        }
        return ValueTask.FromResult<PluginHostCallOutcome>(Returned());
    }

    private static PluginHostCallOutcome.Returned Returned() => new(new PluginValue.Nil());
}
