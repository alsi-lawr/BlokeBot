using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal interface IConfigurationActivationObserver
{
    ValueTask FeatureEnabledAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken cancellationToken
    );
}
