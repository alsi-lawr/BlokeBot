using BlokeBot.Core.Auth.Sessions;
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

    protected void ReportUiFault(string operation, Exception exception)
    {
        UiFaults.Report(
            exception,
            new UiFaultContext(GetType().Name, operation, HostId == 0 ? null : HostId, null)
        );
    }

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
