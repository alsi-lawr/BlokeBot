using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.TwitchOperations.Shared;

public partial class NativeTwitchToolSwitcher
{
    [Inject]
    private BlokeBot.Core.Features.HostedChannels.HostFeatureService _features { get; set; } =
        null!;

    [Parameter]
    public int HostId { get; set; }

    private bool _polls;
    private bool _clips;
    private bool _rewards;
    private bool _predictions;

    protected override async Task OnParametersSetAsync()
    {
        if (HostId == 0)
        {
            return;
        }

        var result = await _features.Load(HostId).ExecuteAsync(CancellationToken.None);
        _ = result.Match(
            option =>
            {
                _ = option.Match(
                    enabled =>
                    {
                        _polls =
                            (enabled & BlokeBot.Persistence.Models.HostFeatureFlags.Polls)
                            == BlokeBot.Persistence.Models.HostFeatureFlags.Polls;
                        _clips =
                            (enabled & BlokeBot.Persistence.Models.HostFeatureFlags.ClipsAndMarkers)
                            == BlokeBot.Persistence.Models.HostFeatureFlags.ClipsAndMarkers;
                        _rewards =
                            (
                                enabled
                                & BlokeBot.Persistence.Models.HostFeatureFlags.RewardsAndRedemptions
                            ) == BlokeBot.Persistence.Models.HostFeatureFlags.RewardsAndRedemptions;
                        _predictions =
                            (enabled & BlokeBot.Persistence.Models.HostFeatureFlags.Predictions)
                            == BlokeBot.Persistence.Models.HostFeatureFlags.Predictions;
                        return true;
                    },
                    () => false
                );
                return true;
            },
            _ => false
        );
    }
}
