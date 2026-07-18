using System.Net;
using System.Text;

namespace BlokeBot.Core.Auth;

internal static class BlokeBotAuthResultPage
{
    public static IResult Render(BlokeBotAuthResult result)
    {
        var encode = (string value) => WebUtility.HtmlEncode(value);
        var view = View(result.Outcome);
        var retry = RetryAction(result.RetryAction) is { } action
            ? $"<a class=\"button button-primary\" href=\"{action.Url}\">{action.Text}</a>"
            : string.Empty;
        var support = result.SupportReference is { } reference
            ? $"<p class=\"support-reference\">Support reference: <code>{encode(reference)}</code></p><a class=\"support\" href=\"https://github.com/alsi-lawr/BlokeBot/issues\">Get help</a>"
            : string.Empty;
        var returnAction = ReturnAction(result.ReturnAction);
        var role = result.Outcome == BlokeBotAuthOutcome.Success ? "status" : "alert";
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{view.Title}} | BlokeBot</title>
                <script>
                    (() => {
                        const storageKey = "blokebot.theme";
                        const media = window.matchMedia("(prefers-color-scheme: dark)");
                        const valid = value => value === "dark" || value === "light";

                        const storedTheme = () => {
                            try {
                                const value = window.localStorage.getItem(storageKey);
                                return valid(value) ? value : null;
                            } catch {
                                return null;
                            }
                        };

                        const theme = storedTheme() ?? (media.matches ? "dark" : "light");
                        document.documentElement.dataset.theme = theme;
                        document.documentElement.style.colorScheme = theme;
                    })();
                </script>
                <style>
                    :root { font-family: Inter, ui-sans-serif, system-ui, sans-serif; background: #f8fafc; color: #172033; }
                    body { margin: 0; }
                    main { box-sizing: border-box; display: grid; min-height: 100vh; place-items: center; padding: 1rem; }
                    article { box-sizing: border-box; width: min(100%, 34rem); border: 1px solid #dbe3ef; border-radius: .875rem; background: #fff; box-shadow: 0 12px 32px rgb(15 23 42 / 12%); padding: clamp(1.25rem, 5vw, 2rem); }
                    .brand { color: #6d28d9; font-size: .75rem; font-weight: 800; letter-spacing: .16em; text-transform: uppercase; }
                    h1 { margin: .6rem 0 1rem; font-size: clamp(1.35rem, 6vw, 1.75rem); line-height: 1.2; }
                    p { margin: .75rem 0; line-height: 1.5; }
                    .change { font-weight: 700; }
                    .actions { display: grid; gap: .625rem; margin-top: 1.5rem; }
                    .button { box-sizing: border-box; min-height: 2.75rem; border: 1px solid #cbd5e1; border-radius: .5rem; padding: .625rem .875rem; color: inherit; font: inherit; font-weight: 700; line-height: 1.4; text-align: center; text-decoration: none; }
                    .button-primary { border-color: #6d28d9; background: #6d28d9; color: #fff; }
                    button { cursor: pointer; background: transparent; }
                    .button:focus-visible, .support:focus-visible { outline: 3px solid #7c3aed; outline-offset: 3px; }
                    .support, code { color: #5b21b6; }
                    .support-reference { font-size: .875rem; }
                    @media (min-width: 30rem) { .actions { grid-template-columns: repeat(2, minmax(0, 1fr)); } .actions .button:last-child:nth-child(3) { grid-column: 1 / -1; } }
                    html[data-theme="dark"] { background: #0f172a; color: #e2e8f0; }
                    html[data-theme="dark"] article { border-color: #334155; background: #182235; box-shadow: 0 12px 32px rgb(0 0 0 / 28%); }
                    html[data-theme="dark"] .button { border-color: #475569; }
                    html[data-theme="dark"] .button-primary { border-color: #7c3aed; background: #7c3aed; }
                    html[data-theme="dark"] .support, html[data-theme="dark"] code { color: #c4b5fd; }
                </style>
            </head>
            <body>
                <main>
                    <article>
                        <div class="brand">BlokeBot</div>
                        <section role="{{role}}">
                            <h1>{{view.Title}}</h1>
                            <p>{{view.Message}}</p>
                            <p class="change">No changes were made.</p>
                            <p>{{view.NextAction}}</p>
                        </section>
                        <div class="actions">
                            {{retry}}
                            <a class="button" href="{{returnAction.Url}}">{{returnAction.Text}}</a>
                            <button class="button" type="button" onclick="window.close()">Close window</button>
                        </div>
                        {{support}}
                    </article>
                </main>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html", Encoding.UTF8, (int)result.Status);
    }

    private static BlokeBotAuthResultView View(BlokeBotAuthOutcome outcome)
    {
        return outcome switch
        {
            BlokeBotAuthOutcome.Success => new(
                "Twitch access saved",
                "BlokeBot has saved this Twitch connection.",
                "You can return or close this window."
            ),
            BlokeBotAuthOutcome.Cancelled => new(
                "Connection cancelled",
                "Twitch did not finish this connection.",
                "Try again when you are ready."
            ),
            BlokeBotAuthOutcome.InvalidOrExpired => new(
                "Connection link expired",
                "This Twitch connection link is no longer valid.",
                "Start a new connection to continue."
            ),
            BlokeBotAuthOutcome.PermissionOrAccount => new(
                "Twitch access needed",
                "Use the required Twitch account and approve every requested permission.",
                "Try again after checking the Twitch account and permissions."
            ),
            BlokeBotAuthOutcome.ProviderUnavailable => new(
                "Twitch is temporarily unavailable",
                "BlokeBot could not finish this connection right now.",
                "Try again in a few minutes."
            ),
            BlokeBotAuthOutcome.Unavailable => new(
                "Twitch connection unavailable",
                "This Twitch connection is not available yet.",
                "An administrator needs to check the connection settings."
            ),
            BlokeBotAuthOutcome.AccessRequired => new(
                "Access required",
                "You do not have access to complete this Twitch connection.",
                "Ask the channel owner or a BlokeBot administrator for help."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };
    }

    private static BlokeBotAuthResultAction? RetryAction(BlokeBotAuthRetryAction action)
    {
        return action switch
        {
            BlokeBotAuthRetryAction.None => null,
            BlokeBotAuthRetryAction.SignIn => new("/auth/login?start=true", "Try again"),
            BlokeBotAuthRetryAction.BotAccount => new("/oauth/start", "Try again"),
            BlokeBotAuthRetryAction.ChannelBot => new("/oauth/channel-bot/start", "Try again"),
            BlokeBotAuthRetryAction.HostBot => new("/oauth/host-bot/start", "Try again"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }

    private static BlokeBotAuthResultAction ReturnAction(BlokeBotAuthReturnAction action)
    {
        return action switch
        {
            BlokeBotAuthReturnAction.SignIn => new("/auth/login", "Return to sign in"),
            BlokeBotAuthReturnAction.ChannelSetup => new("/host", "Return to Channel setup"),
            BlokeBotAuthReturnAction.Admin => new("/admin", "Return to Admin"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }

    private sealed record BlokeBotAuthResultView(string Title, string Message, string NextAction);

    private sealed record BlokeBotAuthResultAction(string Url, string Text);
}
