using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public partial class ProfileSettingsSection
{
    [Parameter, EditorRequired]
    public GuessingConfiguration Configuration { get; set; } = null!;

    [Parameter, EditorRequired]
    public EventCallback CreateProfile { get; set; }

    [Parameter, EditorRequired]
    public EventCallback DeleteProfile { get; set; }

    [Parameter]
    public string NewProfileName { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewProfileNameChanged { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ChangeEventArgs> SelectProfile { get; set; }

    private async Task OnNewProfileNameChanged(ChangeEventArgs args)
    {
        NewProfileName = args.Value?.ToString() ?? string.Empty;
        await NewProfileNameChanged.InvokeAsync(NewProfileName);
    }

    private async Task InvokeCreateProfileAsync() => await CreateProfile.InvokeAsync();

    private async Task InvokeDeleteProfileAsync() => await DeleteProfile.InvokeAsync();
}
