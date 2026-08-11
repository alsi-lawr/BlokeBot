using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

/// <summary>
/// One immutable BLOKEBOT-D-206 unlock record. The Equipped pill reports the viewer's current
/// selection beside it without merging the two.
/// </summary>
public partial class CommunityUnlockRow
{
    [Inject]
    private TimeProvider _clock { get; set; } = default!;

    [Parameter, EditorRequired]
    public required CommunityUnlockView Unlock { get; set; }

    private string _login => $"@{Unlock.Login}";

    private DateTime _nowUtc => _clock.GetUtcNow().UtcDateTime;
}
