using System.Net;

namespace BlokeBot.Auth.Web;

internal static class LoginPage
{
    private const string ThemeScript = """
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

    public static string Render(string? error = null)
    {
        var errorBlock = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"""
                  <div class="mt-5 rounded-md border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">
                      {WebUtility.HtmlEncode(error)}
                  </div>
                """;

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Authenticate with Twitch</title>
                {{ThemeScript}}
                <link rel="stylesheet" href="/app.css" />
            </head>
            <body class="min-h-screen bg-background text-foreground">
                <main class="flex min-h-screen items-center justify-center px-4">
                    <section class="surface w-full max-w-md rounded-lg p-8 shadow-xl shadow-slate-200/70">
                        <div class="mb-7">
                            <div class="flex items-center gap-3">
                                <img class="surface-muted h-10 w-10 rounded-lg p-1" src="/blokedroid.svg" alt="" />
                                <p class="text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">BlokeBot</p>
                            </div>
                            <h1 class="mt-2 text-2xl font-semibold tracking-tight text-foreground">Authenticate with Twitch</h1>
                        </div>
                        <a class="btn-primary h-11 w-full" href="/auth/login?start=true">
                            Authenticate with Twitch
                        </a>
                        {{errorBlock}}
                    </section>
                </main>
            </body>
            </html>
            """;
    }
}
