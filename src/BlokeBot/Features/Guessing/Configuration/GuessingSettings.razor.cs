using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostConfig.Page;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.Replies;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Guessing.Configuration;

public partial class GuessingSettings
{
    private static readonly IReadOnlyList<ReplyDeliveryOption> WhisperReplyOptions =
    [
        new("Round already open", GuessingReplyKeys.RoundAlreadyOpen),
        new("No open round", GuessingReplyKeys.NoOpenRound),
        new("Guessing already stopped", GuessingReplyKeys.GuessingAlreadyStopped),
        new("Guessing closed", GuessingReplyKeys.GuessingClosed),
        new("Invalid guess", GuessingReplyKeys.InvalidGuess),
        new("Guess usage", GuessingReplyKeys.GuessUsage),
        new("Available guesses", GuessingReplyKeys.AvailableGuesses),
        new("Win usage", GuessingReplyKeys.WinUsage),
        new("Moderator only", GuessingReplyKeys.ModeratorOnly),
    ];

    private GuessingConfiguration? config;
    private bool featureEnabled;
    private string newProfileName = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            Events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                work => InvokeAsync(work),
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await LoadPageContextAsync();
        featureEnabled =
            HostId != 0
            && await Features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Guessing,
                CancellationToken.None
            );
        config = featureEnabled
            ? await Configuration.LoadConfigurationAsync(HostId, null, CancellationToken.None)
            : null;
    }

    private void AddOption()
    {
        config?.Profile.Options.Add(new GuessOptionEditor());
    }

    private void RemoveOption(GuessOptionEditor option)
    {
        config?.Profile.Options.Remove(option);
    }

    private async Task CreateProfileAsync()
    {
        var result = await Configuration.CreateProfileAsync(
            HostId,
            newProfileName,
            CancellationToken.None
        );
        PublishResult(result);
        newProfileName = string.Empty;
        config = await Configuration.LoadConfigurationAsync(HostId, null, CancellationToken.None);
    }

    private async Task DeleteProfileAsync()
    {
        if (config is null)
            return;

        var result = await Configuration.DeleteProfileAsync(
            HostId,
            config.Profile.Id,
            CancellationToken.None
        );
        PublishResult(result);
        config = await Configuration.LoadConfigurationAsync(HostId, null, CancellationToken.None);
    }

    private async Task SaveAsync()
    {
        if (config is null)
            return;

        try
        {
            await Configuration.SaveConfigurationAsync(HostId, config, CancellationToken.None);
            var selectedId = config.Profile.Id;
            config = await Configuration.LoadConfigurationAsync(
                HostId,
                selectedId,
                CancellationToken.None
            );
            Toasts.Success("Settings saved.");
        }
        catch (InvalidOperationException ex)
        {
            Toasts.Error(ex.Message);
        }
    }

    private async Task SelectProfileAsync(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), out var profileId))
            return;

        config = await Configuration.LoadConfigurationAsync(
            HostId,
            profileId,
            CancellationToken.None
        );
    }

    private void PublishResult(GuessingOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
            return;

        Toasts.Publish(result.Succeeded ? ToastKind.Success : ToastKind.Warning, result.Message);
    }
}
