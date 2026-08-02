using Microsoft.JSInterop;

namespace BlokeBot.Core.Components.Layout;

public partial class ThemeToggle
{
    private bool _isDark;
    private bool _isReady;

    private string _buttonClass =>
        $"theme-toggle {(_isDark ? "theme-toggle--dark" : "theme-toggle--light")} {(_isReady ? "theme-toggle--ready" : string.Empty)}";

    private string _label => _isDark ? "Switch to light mode" : "Switch to dark mode";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var theme = await _js.InvokeAsync<string>("blokeBotTheme.current");
        _isDark = string.Equals(theme, "dark", StringComparison.Ordinal);
        _isReady = true;
        StateHasChanged();
    }

    private async Task ToggleAsync()
    {
        var theme = await _js.InvokeAsync<string>("blokeBotTheme.toggle");
        _isDark = string.Equals(theme, "dark", StringComparison.Ordinal);
        _isReady = true;
    }
}
