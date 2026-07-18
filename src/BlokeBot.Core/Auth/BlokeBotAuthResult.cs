namespace BlokeBot.Core.Auth;

internal sealed record BlokeBotAuthResult(
    string Title,
    string Message,
    string ChangeSummary,
    string NextAction,
    BlokeBotAuthResultSeverity Severity,
    int StatusCode,
    BlokeBotAuthResultAction? RetryAction,
    BlokeBotAuthResultAction ReturnAction,
    string? SupportReference
) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        return BlokeBotAuthResultPage.Render(this).ExecuteAsync(httpContext);
    }
}

internal enum BlokeBotAuthResultSeverity
{
    Success,
    Failure,
}

internal sealed record BlokeBotAuthResultAction(string Url, string Text);
