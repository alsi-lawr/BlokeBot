using System.Net;

namespace BlokeBot.Auth.Web;

internal static class LoginPage
{
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
                <link rel="stylesheet" href="/app.css" />
            </head>
            <body class="min-h-screen bg-slate-50 text-slate-950">
                <main class="flex min-h-screen items-center justify-center px-4">
                    <section class="w-full max-w-md rounded-lg border border-slate-200 bg-white p-8 shadow-2xl shadow-slate-200/70">
                        <div class="mb-7">
                            <div class="flex items-center gap-3">
                                <img class="h-10 w-10 rounded-lg border border-slate-200 bg-white p-1" src="/blokedroid.svg" alt="" />
                                <p class="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">BlokeBot</p>
                            </div>
                            <h1 class="mt-2 text-2xl font-semibold tracking-tight text-slate-950">Authenticate with Twitch</h1>
                        </div>
                        <a class="inline-flex h-11 w-full items-center justify-center rounded-md bg-[#9146ff] px-4 text-sm font-semibold text-white shadow-lg shadow-purple-200 transition hover:bg-[#7c3aed] focus:outline-none focus:ring-2 focus:ring-[#9146ff] focus:ring-offset-2" href="/auth/login?start=true">
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
