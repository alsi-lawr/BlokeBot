using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.MomentAttachments;

public partial class PublicMomentAttachments
{
    private MomentAttachmentPublicProjection? _projection;

    [Inject]
    private MomentAttachmentService _attachments { get; set; } = null!;

    [Parameter]
    [EditorRequired]
    public string Channel { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public MomentAttachmentDestination Destination { get; set; } =
        new MomentAttachmentDestination.Bounty(Guid.Empty);

    protected override async Task OnParametersSetAsync() =>
        _projection = await _attachments.GetPublicAsync(
            Channel,
            Destination,
            CancellationToken.None
        );
}
