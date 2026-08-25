using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginPointsHostModule(
    HostFeatureService features,
    PointBalanceService balances
) : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Points;

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var context = (PluginInvocationContext.Channel)call.Context;
        if (
            !await features.IsEnabledAsync(
                context.Host.Value,
                HostFeatureFlags.Points,
                cancellationToken
            )
        )
        {
            return Failed(PluginHostFailureCode.Unavailable, "Points are disabled.");
        }
        var amount = PointAmount.ParseNonNegativeAbsolute(
            ((PluginValue.String)call.Arguments[1]).Value
        );
        var actor = context.Actor?.Login ?? context.Plugin.PluginId.Value;
        return await amount.Match<ValueTask<PluginHostCallOutcome>>(
            ApplyAsync,
            static _ =>
                ValueTask.FromResult<PluginHostCallOutcome>(
                    Failed(PluginHostFailureCode.InvalidArguments, "Point amount is invalid.")
                )
        );

        async ValueTask<PluginHostCallOutcome> ApplyAsync(PointAmount parsed)
        {
            var result = await balances
                .Add(
                    context.Host.Value,
                    ((PluginValue.String)call.Arguments[0]).Value,
                    parsed,
                    actor,
                    ((PluginValue.String)call.Arguments[2]).Value
                )
                .ExecuteAsync(cancellationToken);
            return result.Match<PluginHostCallOutcome>(
                mutation => new PluginHostCallOutcome.Returned(
                    new PluginValue.String(mutation.Balance.ToString())
                ),
                static _ =>
                    Failed(PluginHostFailureCode.ProviderRejected, "Point change was rejected.")
            );
        }
    }

    private static PluginHostCallOutcome.Failed Failed(
        PluginHostFailureCode code,
        string message
    ) => new(new(code, message));
}
