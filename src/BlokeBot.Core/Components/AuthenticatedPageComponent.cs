using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Core.Components;

public abstract class AuthenticatedPageComponent : ComponentBase, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    [CascadingParameter]
    protected Task<AuthenticationState> AuthenticationState { get; set; } =
        Task.FromResult(new AuthenticationState(new()));

    [Inject]
    protected BlokeBotPageContextAccessor PageContexts { get; set; } = default!;

    [Inject]
    protected UiFaultTelemetry UiFaults { get; set; } = default!;

    [Inject]
    protected IServiceProvider Services { get; set; } = default!;

    protected BlokeBotPageContext PageContext { get; private set; } = BlokeBotPageContext.Anonymous;

    protected BotHostChoice? Host { get; private set; }

    protected int HostId => Host?.Id ?? 0;

    protected string HostLogin => Host?.Login ?? string.Empty;

    protected string ActorLogin { get; private set; } = string.Empty;

    protected async Task<BlokeBotPageContext> LoadPageContextAsync()
    {
        PageContext = await PageContexts.FromAsync(AuthenticationState);
        Host = PageContext.Session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        ActorLogin = PageContext.ActorLogin;
        return PageContext;
    }

    protected T TrackSubscription<T>(T subscription)
        where T : IDisposable
    {
        _subscriptions.Add(subscription);
        return subscription;
    }

    protected Exception? RouteLoadFailure { get; private set; }

    protected async Task ObserveRouteLoadAsync(Func<Task> load)
    {
        RouteLoadFailure = null;

        try
        {
            await load();
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(ObserveRouteLoadAsync), exception);
            RouteLoadFailure = exception;
        }
    }

    protected async Task ObserveUiOperationAsync(string operation, Func<Task> execute)
    {
        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            ReportUiFault(operation, exception);
            throw;
        }
    }

    protected async Task<T> ObserveUiOperationAsync<T>(string operation, Func<Task<T>> execute)
    {
        try
        {
            return await execute();
        }
        catch (Exception exception)
        {
            ReportUiFault(operation, exception);
            throw;
        }
    }

    protected async Task RunSelectedHostMutationAsync(int requestedHostId, Func<Task> mutation)
    {
        var pageContext = await LoadPageContextAsync();
        var selectedHost = pageContext.Session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        if (
            selectedHost?.Id == requestedHostId
            && selectedHost.Role is AuthRole.Streamer or AuthRole.Admin
        )
        {
            await mutation();
            return;
        }

        var authority = await Services
            .GetRequiredService<ModeratorAuthorityService>()
            .AuthorizeAsync(pageContext.Session, requestedHostId, CancellationToken.None);
        await authority.Match(
            _ => mutation(),
            _ => RecoverModeratorAccessAsync(requestedHostId),
            _ =>
            {
                Services
                    .GetRequiredService<ToastService>()
                    .Publish(
                        ToastRequest<ErrorToastStrategy>.WithTitle(
                            "Your selected channel changed. Choose a channel and try again.",
                            "Channel not selected"
                        )
                    );
                return Task.CompletedTask;
            },
            _ =>
            {
                Services
                    .GetRequiredService<ToastService>()
                    .Publish(
                        ToastRequest<WarningToastStrategy>.WithTitle(
                            "BlokeBot could not confirm your moderator access. Try again, or find channels and sign in again.",
                            "Moderator access needs checking"
                        )
                    );
                return Task.CompletedTask;
            }
        );
    }

    private Task RecoverModeratorAccessAsync(int hostId)
    {
        Services
            .GetRequiredService<ToastService>()
            .Publish(
                ToastRequest<ErrorToastStrategy>.WithTitle(
                    "You no longer have moderator access to this channel. Find channels and sign in again.",
                    "Moderator access removed"
                )
            );
        Services
            .GetRequiredService<NavigationManager>()
            .NavigateTo(
                $"/auth/recover-moderator-access?hostId={hostId}&returnUrl={Uri.EscapeDataString(CurrentPath())}",
                forceLoad: true
            );
        return Task.CompletedTask;
    }

    private string CurrentPath()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        return "/" + navigation.ToBaseRelativePath(navigation.Uri);
    }

    protected void ReportUiFault(string operation, Exception exception) =>
        UiFaults.Report(
            exception,
            new UiFaultContext(GetType().Name, operation, HostId == 0 ? null : HostId, null)
        );

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        foreach (var subscription in _subscriptions.AsEnumerable().Reverse())
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}
