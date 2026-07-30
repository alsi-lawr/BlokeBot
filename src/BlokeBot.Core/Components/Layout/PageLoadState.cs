namespace BlokeBot.Core.Components.Layout;

public abstract record PageLoadState
{
    private PageLoadState() { }

    public sealed record Ready : PageLoadState;

    public sealed record Loading(string Message) : PageLoadState;

    public sealed record Failure(string Message, Func<Task> RetryAsync) : PageLoadState;
}
