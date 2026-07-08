using BlokeBot.Auth.Sessions;
using BlokeBot.Hosts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Components;

public abstract class AuthenticatedPageComponent : ComponentBase, IDisposable
{
    private readonly List<IDisposable> subscriptions = [];

    [CascadingParameter]
    protected Task<AuthenticationState> AuthenticationState { get; set; } =
        Task.FromResult(new AuthenticationState(new()));

    [Inject]
    protected BlokeBotPageContextAccessor PageContexts { get; set; } = default!;

    protected BlokeBotPageContext PageContext { get; private set; } = BlokeBotPageContext.Anonymous;

    protected BotHostChoice? Host { get; private set; }

    protected int HostId => Host?.Id ?? 0;

    protected string HostLogin => Host?.Login ?? string.Empty;

    protected string ActorLogin { get; private set; } = string.Empty;

    protected async Task<BlokeBotPageContext> LoadPageContextAsync()
    {
        PageContext = await PageContexts.FromAsync(AuthenticationState);
        Host = PageContext.SelectedHost;
        ActorLogin = PageContext.ActorLogin;
        return PageContext;
    }

    protected T TrackSubscription<T>(T subscription)
        where T : IDisposable
    {
        subscriptions.Add(subscription);
        return subscription;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        foreach (var subscription in subscriptions.AsEnumerable().Reverse())
            subscription.Dispose();

        subscriptions.Clear();
    }
}
