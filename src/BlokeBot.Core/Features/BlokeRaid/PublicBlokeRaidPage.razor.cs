using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.BlokeRaid;

public partial class PublicBlokeRaidPage
{
    private BlokeRaidPublicView? _view;
    private bool _loaded;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        _view = await _raids.LoadPublicAsync(Channel, CancellationToken.None);
        _loaded = true;
    }

    private static int Percent(int value, int maximum) =>
        maximum == 0 ? 0 : (int)Math.Round(value * 100d / maximum);
}
