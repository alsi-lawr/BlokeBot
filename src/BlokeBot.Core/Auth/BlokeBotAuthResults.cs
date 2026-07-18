namespace BlokeBot.Core.Auth;

internal static class BlokeBotAuthResults
{
    private const string _channelSetupUrl = "/host";

    public static BlokeBotAuthResult ConnectionSaved(string returnUrl, string returnActionText)
    {
        return Success(
            "Twitch access saved",
            "BlokeBot has saved this Twitch connection.",
            "Your channel settings have been updated.",
            "You can return to Channel setup or close this window.",
            returnUrl,
            returnActionText
        );
    }

    public static BlokeBotAuthResult BotAccountConnectionSaved()
    {
        return Success(
            "Bot account connected",
            "BlokeBot has saved Twitch access for the bot account.",
            "The bot account connection has been updated.",
            "You can return to Admin or close this window.",
            "/admin",
            "Return to Admin"
        );
    }

    public static BlokeBotAuthResult BotAccountConnectionCancelled()
    {
        return Failure(
            "Connection cancelled",
            "Twitch did not connect the BlokeBot bot account.",
            "No changes were made.",
            "A BlokeBot administrator can try again when they are ready.",
            StatusCodes.Status400BadRequest,
            "/oauth/start",
            "/admin",
            "Return to Admin"
        );
    }

    public static BlokeBotAuthResult BotAccountConnectionExpired()
    {
        return Failure(
            "Connection expired",
            "That bot-account connection has expired.",
            "No changes were made.",
            "A BlokeBot administrator can start a new connection.",
            StatusCodes.Status400BadRequest,
            "/oauth/start",
            "/admin",
            "Return to Admin"
        );
    }

    public static BlokeBotAuthResult BotAccountProviderTemporarilyUnavailable(
        string supportReference
    )
    {
        return Failure(
            "Twitch is temporarily unavailable",
            "BlokeBot could not finish connecting the bot account right now.",
            "No changes were made.",
            "A BlokeBot administrator can try again in a few minutes. If this keeps happening, use the support reference below.",
            StatusCodes.Status502BadGateway,
            "/oauth/start",
            "/admin",
            "Return to Admin",
            supportReference
        );
    }

    public static BlokeBotAuthResult Cancelled(string tryAgainUrl)
    {
        return Failure(
            "Connection cancelled",
            "Twitch did not connect the bot to this channel.",
            "No changes were made.",
            "The channel owner can try again when they are ready.",
            StatusCodes.Status400BadRequest,
            tryAgainUrl,
            _channelSetupUrl,
            "Return to Channel setup"
        );
    }

    public static BlokeBotAuthResult Expired(string tryAgainUrl)
    {
        return Failure(
            "Connection expired",
            "That Twitch connection link has expired.",
            "No changes were made.",
            "The channel owner can start a new connection.",
            StatusCodes.Status400BadRequest,
            tryAgainUrl,
            _channelSetupUrl,
            "Return to Channel setup"
        );
    }

    public static BlokeBotAuthResult WrongChannelAccount(
        string requiredChannelLogin,
        string tryAgainUrl
    )
    {
        return Failure(
            "Use the channel account",
            $"@{requiredChannelLogin} is the Twitch account needed for this channel.",
            "No changes were made.",
            "The channel owner needs to reconnect the bot using that account.",
            StatusCodes.Status400BadRequest,
            tryAgainUrl,
            _channelSetupUrl,
            "Return to Channel setup"
        );
    }

    public static BlokeBotAuthResult PermissionNeeded(string tryAgainUrl)
    {
        return Failure(
            "More Twitch access is needed",
            "Twitch did not give BlokeBot the access this channel needs.",
            "No changes were made.",
            "Try again and approve every permission Twitch shows.",
            StatusCodes.Status400BadRequest,
            tryAgainUrl,
            _channelSetupUrl,
            "Return to Channel setup"
        );
    }

    public static BlokeBotAuthResult ProviderTemporarilyUnavailable(
        string tryAgainUrl,
        string supportReference
    )
    {
        return Failure(
            "Twitch is temporarily unavailable",
            "BlokeBot could not finish this connection right now.",
            "No changes were made.",
            "Try again in a few minutes. If this keeps happening, get help from BlokeBot support.",
            StatusCodes.Status502BadGateway,
            tryAgainUrl,
            _channelSetupUrl,
            "Return to Channel setup",
            supportReference
        );
    }

    public static BlokeBotAuthResult ConnectionUnavailable(
        string returnUrl,
        string returnActionText
    )
    {
        return Failure(
            "Twitch connection unavailable",
            "This Twitch connection is not available yet.",
            "No changes were made.",
            "A BlokeBot administrator needs to check the connection settings.",
            StatusCodes.Status503ServiceUnavailable,
            null,
            returnUrl,
            returnActionText
        );
    }

    public static BlokeBotAuthResult CustomBotMustBeEnabled()
    {
        return Failure(
            "Turn on the custom bot first",
            "Turn on the custom bot before connecting it to Twitch.",
            "No changes were made.",
            "The channel owner can turn on the custom bot in Channel setup, then try again.",
            StatusCodes.Status400BadRequest,
            null,
            _channelSetupUrl,
            "Return to Channel setup"
        );
    }

    public static BlokeBotAuthResult NoChannelSelected()
    {
        return Failure(
            "Choose a channel to continue",
            "Choose a channel to continue",
            "No changes were made.",
            "Open Channel setup, choose your channel, then try again.",
            StatusCodes.Status403Forbidden,
            null,
            _channelSetupUrl,
            "Return to Channel setup"
        );
    }

    public static BlokeBotAuthResult OperatorAccessRequired()
    {
        return Failure(
            "Access needs to be granted",
            "The channel owner or server administrator must grant you access before you can reconnect the bot.",
            "No changes were made.",
            "Ask the channel owner or server administrator to grant access, then try again.",
            StatusCodes.Status403Forbidden,
            null,
            _channelSetupUrl,
            "Return to Channel setup"
        );
    }

    public static BlokeBotAuthResult AdministratorAccessRequired()
    {
        return Failure(
            "Administrator access needed",
            "Only a BlokeBot administrator can open this page.",
            "No changes were made.",
            "Ask an administrator to reconnect the bot.",
            StatusCodes.Status403Forbidden,
            null,
            "/admin",
            "Return to Admin"
        );
    }

    public static BlokeBotAuthResult SignInUnavailable()
    {
        return Failure(
            "Twitch sign-in is unavailable",
            "Twitch sign-in is not set up yet.",
            "No changes were made.",
            "A BlokeBot administrator needs to check the sign-in settings.",
            StatusCodes.Status503ServiceUnavailable,
            null,
            "/auth/login",
            "Return to sign in"
        );
    }

    public static BlokeBotAuthResult SignInNotConfigured()
    {
        return Failure(
            "Twitch sign-in is unavailable",
            "Twitch sign-in is not set up yet.",
            "No changes were made.",
            "A BlokeBot administrator needs to check the sign-in settings.",
            StatusCodes.Status403Forbidden,
            null,
            "/auth/login",
            "Return to sign in"
        );
    }

    public static BlokeBotAuthResult SignInCancelled()
    {
        return Failure(
            "Sign-in cancelled",
            "Twitch did not finish signing you in.",
            "No changes were made.",
            "You can try signing in again when you are ready.",
            StatusCodes.Status400BadRequest,
            "/auth/login?start=true",
            "/auth/login",
            "Return to sign in"
        );
    }

    public static BlokeBotAuthResult SignInProviderFailure(string supportReference)
    {
        return Failure(
            "Twitch sign-in failed",
            "Twitch could not finish signing you in.",
            "No changes were made.",
            "Try signing in again. If this keeps happening, use the support reference below.",
            StatusCodes.Status400BadRequest,
            "/auth/login?start=true",
            "/auth/login",
            "Return to sign in",
            supportReference
        );
    }

    public static BlokeBotAuthResult SignInExpired()
    {
        return Failure(
            "Sign-in expired",
            "That Twitch sign-in link has expired.",
            "No changes were made.",
            "Start a new sign-in to continue.",
            StatusCodes.Status400BadRequest,
            "/auth/login?start=true",
            "/auth/login",
            "Return to sign in"
        );
    }

    public static BlokeBotAuthResult SignInAccessDenied(string message)
    {
        return Failure(
            "Twitch sign-in was not approved",
            message,
            "No changes were made.",
            "Use a Twitch account with BlokeBot access, or ask an administrator for help.",
            StatusCodes.Status403Forbidden,
            "/auth/login?start=true",
            "/auth/login",
            "Return to sign in"
        );
    }

    public static BlokeBotAuthResult SignInProviderTemporarilyUnavailable(string supportReference)
    {
        return Failure(
            "Twitch is temporarily unavailable",
            "BlokeBot could not finish signing you in right now.",
            "No changes were made.",
            "Try again in a few minutes. If this keeps happening, use the support reference below.",
            StatusCodes.Status502BadGateway,
            "/auth/login?start=true",
            "/auth/login",
            "Return to sign in",
            supportReference
        );
    }

    private static BlokeBotAuthResult Success(
        string title,
        string message,
        string changeSummary,
        string nextAction,
        string returnUrl,
        string returnActionText
    )
    {
        return new(
            title,
            message,
            changeSummary,
            nextAction,
            BlokeBotAuthResultSeverity.Success,
            StatusCodes.Status200OK,
            null,
            new(returnUrl, returnActionText),
            null
        );
    }

    private static BlokeBotAuthResult Failure(
        string title,
        string message,
        string changeSummary,
        string nextAction,
        int statusCode,
        string? retryUrl,
        string returnUrl,
        string returnActionText,
        string? supportReference = null
    )
    {
        return new(
            title,
            message,
            changeSummary,
            nextAction,
            BlokeBotAuthResultSeverity.Failure,
            statusCode,
            retryUrl is null ? null : new(retryUrl, "Try again"),
            new(returnUrl, returnActionText),
            supportReference
        );
    }
}
