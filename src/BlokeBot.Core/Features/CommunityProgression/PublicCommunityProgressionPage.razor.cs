using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

public partial class PublicCommunityProgressionPage
{
    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private CommunityPublicView? _view;
    private bool _loaded;

    protected override async Task OnParametersSetAsync()
    {
        _view = await _progression.GetPublicAsync(Channel, CancellationToken.None);
        _loaded = true;
    }
}
