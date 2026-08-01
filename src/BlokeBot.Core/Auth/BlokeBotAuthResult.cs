namespace BlokeBot.Core.Auth;

internal sealed record BlokeBotAuthResult(
    BlokeBotAuthOutcome Outcome,
    BlokeBotAuthStatus Status,
    BlokeBotAuthRetryAction RetryAction,
    BlokeBotAuthReturnAction ReturnAction,
    string? SupportReference,
    BlokeBotAuthContext? Context = null
) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext) =>
        BlokeBotAuthResultPage.Render(this).ExecuteAsync(httpContext);
}

internal enum BlokeBotAuthOutcome
{
    Success,
    Cancelled,
    InvalidOrExpired,
    PermissionOrAccount,
    WrongAccount,
    ProviderUnavailable,
    Unavailable,
    AccessRequired,
    NoChannelSelected,
    CustomBotDisabled,
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
    Broadcaster,
    HostBot,
}

internal enum BlokeBotAuthReturnAction
{
    SignIn,
    ChannelSetup,
    Admin,
}

internal abstract record BlokeBotAuthContext
{
    private BlokeBotAuthContext() { }

    internal sealed record Success(BlokeBotAuthSuccessKind Kind) : BlokeBotAuthContext;

    internal sealed record RequiredChannel(string Login) : BlokeBotAuthContext;
}

internal enum BlokeBotAuthSuccessKind
{
    ChannelConnection,
    BotAccount,
}
