using System.Net;
using System.Text;

namespace BlokeBot.BotRuntime;

internal static class TwitchConnectionResultPage
{
    private const string _channelSetupUrl = "/host";

    public static IResult ConnectionSaved(string returnUrl, string returnActionText)
    {
        return Render(
            new(
                "Twitch access saved",
                "BlokeBot has saved this Twitch connection.",
                "Your channel settings have been updated.",
                "You can return to Channel setup or close this window.",
                null,
                returnUrl,
                returnActionText,
                StatusCodes.Status200OK,
                false,
                null
            )
        );
    }

    public static IResult Cancelled(string tryAgainUrl)
    {
        return Render(
            new(
                "Connection cancelled",
                "Twitch did not connect the bot to this channel.",
                "No changes were made.",
                "The channel owner can try again when they are ready.",
                tryAgainUrl,
                _channelSetupUrl,
                "Return to Channel setup",
                StatusCodes.Status400BadRequest,
                false,
                null
            )
        );
    }

    public static IResult Expired(string tryAgainUrl)
    {
        return Render(
            new(
                "Connection expired",
                "That Twitch connection link has expired.",
                "No changes were made.",
                "The channel owner can start a new connection.",
                tryAgainUrl,
                _channelSetupUrl,
                "Return to Channel setup",
                StatusCodes.Status400BadRequest,
                false,
                null
            )
        );
    }

    public static IResult WrongChannelAccount(string requiredChannelLogin, string tryAgainUrl)
    {
        return Render(
            new(
                "Use the channel account",
                $"@{requiredChannelLogin} is the Twitch account needed for this channel.",
                "No changes were made.",
                "The channel owner needs to reconnect the bot using that account.",
                tryAgainUrl,
                _channelSetupUrl,
                "Return to Channel setup",
                StatusCodes.Status400BadRequest,
                false,
                null
            )
        );
    }

    public static IResult PermissionNeeded(string tryAgainUrl)
    {
        return Render(
            new(
                "More Twitch access is needed",
                "Twitch did not give BlokeBot the access this channel needs.",
                "No changes were made.",
                "Try again and approve every permission Twitch shows.",
                tryAgainUrl,
                _channelSetupUrl,
                "Return to Channel setup",
                StatusCodes.Status400BadRequest,
                false,
                null
            )
        );
    }

    public static IResult ProviderTemporarilyUnavailable(
        string tryAgainUrl,
        string supportReference
    )
    {
        return Render(
            new(
                "Twitch is temporarily unavailable",
                "BlokeBot could not finish this connection right now.",
                "No changes were made.",
                "Try again in a few minutes. If this keeps happening, get help from BlokeBot support.",
                tryAgainUrl,
                _channelSetupUrl,
                "Return to Channel setup",
                StatusCodes.Status502BadGateway,
                true,
                supportReference
            )
        );
    }

    public static IResult ConnectionUnavailable(string returnUrl, string returnActionText)
    {
        return Render(
            new(
                "Twitch connection unavailable",
                "This Twitch connection is not available yet.",
                "No changes were made.",
                "A BlokeBot administrator needs to check the connection settings.",
                null,
                returnUrl,
                returnActionText,
                StatusCodes.Status503ServiceUnavailable,
                false,
                null
            )
        );
    }

    public static IResult CustomBotMustBeEnabled()
    {
        return Render(
            new(
                "Turn on the custom bot first",
                "Turn on the custom bot before connecting it to Twitch.",
                "No changes were made.",
                "The channel owner can turn on the custom bot in Channel setup, then try again.",
                null,
                _channelSetupUrl,
                "Return to Channel setup",
                StatusCodes.Status400BadRequest,
                false,
                null
            )
        );
    }

    public static IResult NoChannelSelected()
    {
        return Render(
            new(
                "Choose a channel to continue",
                "Choose a channel to continue",
                "No changes were made.",
                "Open Channel setup, choose your channel, then try again.",
                null,
                _channelSetupUrl,
                "Return to Channel setup",
                StatusCodes.Status403Forbidden,
                false,
                null
            )
        );
    }

    public static IResult OperatorAccessRequired()
    {
        return Render(
            new(
                "Access needs to be granted",
                "The channel owner or server administrator must grant you access before you can reconnect the bot.",
                "No changes were made.",
                "Ask the channel owner or server administrator to grant access, then try again.",
                null,
                _channelSetupUrl,
                "Return to Channel setup",
                StatusCodes.Status403Forbidden,
                false,
                null
            )
        );
    }

    public static IResult AdministratorAccessRequired()
    {
        return Render(
            new(
                "Administrator access needed",
                "Only a BlokeBot administrator can open this page.",
                "No changes were made.",
                "Ask an administrator to reconnect the bot.",
                null,
                "/admin",
                "Return to Admin",
                StatusCodes.Status403Forbidden,
                false,
                null
            )
        );
    }

    private static IResult Render(TwitchConnectionResult result)
    {
        var encode = (string value) => WebUtility.HtmlEncode(value);
        var tryAgain = result.TryAgainUrl is { } url
            ? $"<a class=\"button button-primary\" href=\"{encode(url)}\">Try again</a>"
            : string.Empty;
        var support =
            result.ShowSupportReference && result.SupportReference is { } reference
                ? $"<p class=\"support-reference\">Support reference: <code>{encode(reference)}</code></p><a class=\"support\" href=\"https://github.com/alsi-lawr/BlokeBot/issues\">Get help</a>"
                : string.Empty;
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{encode(result.Title)}} | BlokeBot</title>
                <style>
                    :root { color-scheme: light dark; font-family: system-ui, sans-serif; }
                    body { margin: 0; background: #f8fafc; color: #172033; }
                    main { box-sizing: border-box; display: grid; min-height: 100vh; place-items: center; padding: 1.5rem; }
                    article { width: min(100%, 38rem); border: 1px solid #dbe3ef; border-radius: 1rem; background: #fff; box-shadow: 0 18px 50px rgb(15 23 42 / 12%); padding: 2rem; }
                    .brand { color: #6d28d9; font-weight: 800; letter-spacing: .03em; }
                    h1 { margin: .6rem 0 1rem; font-size: 1.75rem; }
                    p { line-height: 1.55; }
                    .change { font-weight: 700; }
                    .actions { display: flex; flex-wrap: wrap; gap: .75rem; margin-top: 1.5rem; }
                    .button { border-radius: .5rem; padding: .7rem 1rem; font-weight: 700; text-decoration: none; }
                    .button-primary { background: #6d28d9; color: white; }
                    .button-secondary { border: 1px solid #cbd5e1; color: #172033; }
                    button { background: transparent; cursor: pointer; font: inherit; }
                    .support { display: inline-block; margin-top: 1rem; color: #5b21b6; }
                    @media (prefers-color-scheme: dark) { body { background: #0f172a; color: #e2e8f0; } article { background: #182235; border-color: #334155; } .button-secondary { border-color: #475569; color: #e2e8f0; } }
                </style>
            </head>
            <body>
                <main>
                    <article>
                        <div class="brand">BlokeBot</div>
                        <h1>{{encode(result.Title)}}</h1>
                        <p>{{encode(result.Message)}}</p>
                        <p class="change">{{encode(result.ChangeSummary)}}</p>
                        <p>{{encode(result.NextAction)}}</p>
                        <div class="actions">
                            {{tryAgain}}
                            <a class="button button-secondary" href="{{encode(
                result.ReturnUrl
            )}}">{{encode(result.ReturnActionText)}}</a>
                            <button class="button button-secondary" type="button" onclick="window.close()">Close window</button>
                        </div>
                        {{support}}
                    </article>
                </main>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html", Encoding.UTF8, result.StatusCode);
    }

    private sealed record TwitchConnectionResult(
        string Title,
        string Message,
        string ChangeSummary,
        string NextAction,
        string? TryAgainUrl,
        string ReturnUrl,
        string ReturnActionText,
        int StatusCode,
        bool ShowSupportReference,
        string? SupportReference
    );
}
