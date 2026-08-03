using BlokeBot.Twitch;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.PublicLeaderboards;

public partial class PublicLeaderboardPrompt
{
    private string _channel = string.Empty;
    private string? _channelError;
    private string _feature = "guessing";
    private ElementReference _channelInput;
    private string? _normalizedInitialChannel;

    [Parameter]
    public string ContainerClass { get; set; } = "mt-6 border-t border-slate-200 pt-5";

    [Parameter]
    public string? InitialChannel { get; set; }

    private string _normalizedChannel => Login.Normalize(_channel);

    protected override void OnParametersSet()
    {
        var normalized = Login.Normalize(InitialChannel);
        if (normalized.Length == 0 || normalized == _normalizedInitialChannel)
        {
            return;
        }

        _normalizedInitialChannel = normalized;
        if (string.IsNullOrWhiteSpace(_channel))
        {
            _channel = normalized;
        }
    }

    private void UpdateChannel(ChangeEventArgs args)
    {
        _channel = args.Value?.ToString() ?? string.Empty;
        _channelError = null;
    }

    private async Task OpenLeaderboard()
    {
        if (_normalizedChannel.Length == 0)
        {
            _channelError = "Enter a Twitch channel name.";
            await _channelInput.FocusAsync();
            return;
        }

        _navigation.NavigateTo(
            $"/{_feature}/leaderboard/{Uri.EscapeDataString(_normalizedChannel)}"
        );
    }
}
