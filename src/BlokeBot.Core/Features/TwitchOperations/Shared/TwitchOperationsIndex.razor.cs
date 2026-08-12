using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.TwitchOperations.Shared;

public partial class TwitchOperationsIndex
{
    protected override void OnInitialized() =>
        _navigation.NavigateTo("/twitch-operations/polls", replace: true);
}
