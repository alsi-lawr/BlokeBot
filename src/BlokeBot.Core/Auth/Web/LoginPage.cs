using System.Net;

namespace BlokeBot.Core.Auth.Web;

internal static class LoginPage
{
    private const string _themeScript = """
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

                const systemTheme = () => media.matches ? "dark" : "light";
                const selectedTheme = () => storedTheme() ?? systemTheme();
                const applyTheme = theme => {
                    document.documentElement.dataset.theme = theme;
                    document.documentElement.style.colorScheme = theme;
                };

                applyTheme(selectedTheme());
            })();
        </script>
        """;

    private const string _leaderboardScript = """
        <script>
            document.addEventListener("DOMContentLoaded", () => {
                const form = document.querySelector("[data-public-leaderboard-form]");
                if (!form) {
                    return;
                }

                form.addEventListener("submit", event => {
                    event.preventDefault();
                    const feature = form.querySelector("[name='feature']")?.value ?? "guessing";
                    const channelInput = form.querySelector("[name='channel']");
                    const channelError = form.querySelector("[data-channel-error]");
                    const channel = (channelInput?.value ?? "")
                        .trim()
                        .replace(/^[@#]+/, "")
                        .toLowerCase();

                    if (!channel) {
                        channelInput?.setAttribute("aria-invalid", "true");
                        channelError?.classList.remove("hidden");
                        channelInput?.focus();
                        return;
                    }

                    window.location.href = `/${feature}/leaderboard/${encodeURIComponent(channel)}`;
                });

                form.querySelector("[name='channel']")?.addEventListener("input", event => {
                    event.currentTarget.setAttribute("aria-invalid", "false");
                    form.querySelector("[data-channel-error]")?.classList.add("hidden");
                });
            });
        </script>
        """;

    public static string Render()
    {
        return RenderPage(string.Empty);
    }

    public static string RenderError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return RenderPage(
            $"""
              <div class="mt-5 rounded-md border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">
                  {WebUtility.HtmlEncode(error)}
              </div>
            """
        );
    }

    private static string RenderPage(string errorBlock)
    {
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Sign in to BlokeBot</title>
            <link rel="icon" type="image/svg+xml" href="/blokebot-mark.svg" />
            {{_themeScript}}
            <link rel="stylesheet" href="/app.css" />
            </head>
            <body class="min-h-screen bg-background text-foreground">
                <main class="flex min-h-screen items-center justify-center px-4">
                    <section class="surface w-full max-w-md rounded-lg p-8 shadow-xl shadow-slate-200/70">
                        <div class="mb-7">
                            <div class="flex items-center gap-3">
                                <img class="surface-muted h-10 w-10 rounded-lg" src="/blokebot-mark.svg" alt="" />
                                <p class="text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">BlokeBot</p>
                            </div>
                            <h1 class="mt-2 text-2xl font-semibold tracking-tight text-foreground">Sign in to BlokeBot</h1>
                        </div>
                        <a class="btn-primary auth-action h-11 w-full" href="/auth/login?start=true">
                            Continue with Twitch
                        </a>
                        <div class="mt-6 border-t border-slate-200 pt-5">
                            <p class="text-sm font-bold text-slate-950">Public leaderboard</p>
                            <form class="mt-3 grid gap-3" data-public-leaderboard-form>
                                <select class="input" name="feature" aria-label="Leaderboard feature">
                                    <option value="guessing">Guessing</option>
                                    <option value="points">Points</option>
                                </select>
                                <div class="space-y-2">
                                    <label class="label" for="public-leaderboard-channel">Twitch channel name</label>
                                    <input class="input"
                                           id="public-leaderboard-channel"
                                           name="channel"
                                           placeholder="samplechannel"
                                           aria-describedby="public-leaderboard-channel-hint public-leaderboard-channel-error"
                                           aria-invalid="false" />
                                    <p id="public-leaderboard-channel-hint" class="text-xs font-medium text-slate-500">You can enter samplechannel, @samplechannel, or #samplechannel.</p>
                                    <p id="public-leaderboard-channel-error"
                                       class="hidden text-sm font-semibold text-red-700"
                                       role="alert"
                                       data-channel-error>Enter a Twitch channel name.</p>
                                </div>
                                <button class="btn-secondary h-10 w-full" type="submit">View leaderboard</button>
                            </form>
                        </div>
                        {{errorBlock}}
                    </section>
                </main>
                {{_leaderboardScript}}
            </body>
            </html>
            """;
    }
}
