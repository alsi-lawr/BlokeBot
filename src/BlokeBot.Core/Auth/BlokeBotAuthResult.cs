namespace BlokeBot.Core.Auth;

internal sealed record BlokeBotAuthResult(
    BlokeBotAuthOutcome Outcome,
    BlokeBotAuthStatus Status,
    BlokeBotAuthRetryAction RetryAction,
    BlokeBotAuthReturnAction ReturnAction,
    string? SupportReference
) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        return BlokeBotAuthResultPage.Render(this).ExecuteAsync(httpContext);
    }
}

internal enum BlokeBotAuthOutcome
{
    Success,
    Cancelled,
    InvalidOrExpired,
    PermissionOrAccount,
    ProviderUnavailable,
    Unavailable,
    AccessRequired,
}

internal enum BlokeBotAuthStatus
{
    Ok = StatusCodes.Status200OK,
    BadRequest = StatusCodes.Status400BadRequest,
    Forbidden = StatusCodes.Status403Forbidden,
    ServiceUnavailable = StatusCodes.Status503ServiceUnavailable,
    BadGateway = StatusCodes.Status502BadGateway,
}

internal enum BlokeBotAuthRetryAction
{
    None,
    SignIn,
    BotAccount,
    ChannelBot,
    HostBot,
}

internal enum BlokeBotAuthReturnAction
{
    SignIn,
    ChannelSetup,
    Admin,
}
