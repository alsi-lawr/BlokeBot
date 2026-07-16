namespace BlokeBot.Core.Features.Toasts;

public sealed record ToastRequest<TStrategy>
    where TStrategy : IToastStrategy
{
    public ToastRequest(string message)
        : this(message, TStrategy.DefaultTitle) { }

    private ToastRequest(string message, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Message = message.Trim();
        Title = title.Trim();
    }

    public string Message { get; }

    public string Title { get; }

    public static ToastRequest<TStrategy> WithTitle(string message, string title)
    {
        return new(message, title);
    }
}
