using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.BlokeRaid;

public partial class PublicBlokeRaidPage
{
    [Inject]
    private BlokeBot.Core.Features.ViewerPortal.Boundary.PublicViewerGate _publicGate { get; set; } =
        null!;

    private BlokeRaidPublicView? _view;
    private bool _loaded;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        _view = null;
        if (!await _publicGate.TryReadAsync(Channel, CancellationToken.None))
        {
            _loaded = true;
            return;
        }
        _view = await _raids.LoadPublicAsync(Channel, CancellationToken.None);
        _loaded = true;
    }

    private static int Percent(int value, int maximum) =>
        maximum == 0 ? 0 : (int)Math.Round(value * 100d / maximum);
}
