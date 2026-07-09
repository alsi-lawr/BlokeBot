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
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Toasts;

public partial class ToastHost
{
    private const int RemovalAnimationDelayMs = 180;
    private readonly Dictionary<Guid, CancellationTokenSource> autoDismissTokens = [];
    private readonly HashSet<Guid> dismissingToastIds = [];
    private CancellationTokenSource disposeToken = new();
    private IReadOnlyList<ToastNotification> visibleToasts = [];

    protected override void OnInitialized()
    {
        Toasts.Changed += OnToastsChanged;
        RefreshToasts();
    }

    public void Dispose()
    {
        Toasts.Changed -= OnToastsChanged;
        disposeToken.Cancel();
        disposeToken.Dispose();
        foreach (var token in autoDismissTokens.Values)
        {
            token.Cancel();
            token.Dispose();
        }
        autoDismissTokens.Clear();
    }

    private async Task BeginDismissAsync(Guid toastId)
    {
        if (!dismissingToastIds.Add(toastId))
            return;

        if (autoDismissTokens.Remove(toastId, out var autoDismissToken))
        {
            autoDismissToken.Cancel();
            autoDismissToken.Dispose();
        }

        await InvokeAsync(StateHasChanged);
        await Task.Delay(RemovalAnimationDelayMs);
        Toasts.Dismiss(toastId);
        dismissingToastIds.Remove(toastId);
    }

    private async Task AutoDismissAsync(Guid toastId, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            await InvokeAsync(() => BeginDismissAsync(toastId));
        }
        catch (OperationCanceledException) { }
    }

    private void OnToastsChanged() => _ = InvokeAsync(RefreshToasts);

    private void RefreshToasts()
    {
        visibleToasts = Toasts.Current;
        ScheduleAutoDismiss();
        StateHasChanged();
    }

    private void ScheduleAutoDismiss()
    {
        var currentIds = visibleToasts.Select(toast => toast.Id).ToHashSet();
        foreach (
            var staleId in autoDismissTokens.Keys.Where(id => !currentIds.Contains(id)).ToArray()
        )
        {
            autoDismissTokens[staleId].Cancel();
            autoDismissTokens[staleId].Dispose();
            autoDismissTokens.Remove(staleId);
        }

        foreach (var toast in visibleToasts)
        {
            if (
                toast.AutoDismissAfter is not { } delay
                || autoDismissTokens.ContainsKey(toast.Id)
                || dismissingToastIds.Contains(toast.Id)
            )
                continue;

            var token = CancellationTokenSource.CreateLinkedTokenSource(disposeToken.Token);
            autoDismissTokens[toast.Id] = token;
            _ = AutoDismissAsync(toast.Id, delay, token.Token);
        }
    }

    private string ToastClass(ToastNotification toast)
    {
        var classes = $"toast-card toast-card--{KindCssClass(toast.Kind)}";
        return dismissingToastIds.Contains(toast.Id) ? $"{classes} toast-card--removing" : classes;
    }

    private static string KindCssClass(ToastKind kind) =>
        kind switch
        {
            ToastKind.Success => "success",
            ToastKind.Warning => "warning",
            ToastKind.Error => "error",
            _ => "status",
        };

    private static string ToastRole(ToastNotification toast) =>
        toast.Kind is ToastKind.Error or ToastKind.Warning ? "alert" : "status";
}
