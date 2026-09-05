namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginLuaSdkPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development/lua-sdk",
            Eyebrow = "Plugin development",
            Title = "Lua SDK",
            Summary = "The generated module gives LuaLS type information.",
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The module provides types for parameters and results.",
                        "It provides types for failures and context.",
                        "It provides types for settings and handler input.",
                    ],
                    Heading = "SDK import",
                    Paragraphs =
                    [
                        "The public module provides named functions. It does not expose a generic host-call function.",
                    ],
                    Code = """
                        local blokebot = require("blokebot")

                        local context = blokebot.context.current()
                        blokebot.responses.chat("Queued for review.")
                        """,
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The generator creates SDK types.",
                        "The generator creates plugin settings.",
                        "The generator creates handler types.",
                        "The generator creates executable no-op skeletons.",
                    ],
                    Heading = "Type generation",
                    Paragraphs =
                    [
                        "The generator reads plugin.toml.",
                        "The generator replaces only files in its marked directory. It does not change Lua files that you own.",
                    ],
                    Steps =
                    [
                        "Run blokebot-plugin generate from the project directory.",
                        "Open .blokebot/lua/5.4/v1/handler-skeletons.lua.",
                        "Copy the required functions into the declared author module.",
                        "Implement each copied function.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "BlokeBotHostFailure contains a stable failure kind.",
                        "BlokeBotHostFailure contains a code.",
                        "BlokeBotHostFailure contains a safe message.",
                        "BlokeBotContext includes the installation context.",
                        "BlokeBotContext includes the channel context.",
                        "BlokeBotContext includes the automation context.",
                        "BlokeBotContext includes the migration context.",
                        "BlokeBotContext includes the page context.",
                    ],
                    Heading = "Generated type definitions",
                    Facts =
                    [
                        new("BlokeBotContext", "BlokeBotContext is a union of the context types."),
                        new(
                            "BlokeBotInstallationSettings",
                            "The installation settings declared in plugin.toml for the current plugin."
                        ),
                        new(
                            "BlokeBotFeatureSettings",
                            "The feature settings declared in plugin.toml for the current host feature."
                        ),
                        new("BlokeBotHostFailure", "The host failure value."),
                        new(
                            "BlokeBotHostCancellation",
                            "The terminal cancellation value for the current call."
                        ),
                        new(
                            "Plugin action input",
                            "One generated class that matches each declared page action exactly."
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Host failures",
                    Paragraphs =
                    [
                        "A successful host function call returns its documented value. A rejected call raises a typed BlokeBotHostFailure value.",
                    ],
                    Code = """
                        local ok, outcome = pcall(function()
                          return blokebot.http.send({
                            method = "GET",
                            url = "https://example.invalid/item",
                          })
                        end)

                        if not ok then
                          blokebot.diagnostics.log("warning", outcome.safeMessage)
                        end
                        """,
                    Note =
                        "Do not handle cancellation as a normal failure. Cancellation ends the current call.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Plugin code cannot choose a different plugin.",
                        "Plugin code cannot choose a different host.",
                        "Plugin code cannot choose a different feature.",
                        "Plugin code cannot choose a different actor.",
                        "Do not expose a protected setting in logs.",
                        "Do not expose a protected setting in diagnostics.",
                        "Do not expose a protected setting in responses.",
                        "Do not expose a protected setting in audit fields.",
                        "Do not expose a protected setting in failure messages.",
                        "Do not expose a protected setting in page documents.",
                    ],
                    Heading = "Context and settings",
                    Paragraphs =
                    [
                        "context.current returns the identity for the current call.",
                        "settings.installation and settings.feature return declared values for the current plugin installation or host feature. Unset optional values are absent.",
                    ],
                    Code = """
                        local context = blokebot.context.current()
                        local settings = blokebot.settings.feature()

                        local message = settings["response-message"] or "Hello."
                        blokebot.responses.chat(message)
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Lua type reference",
                    Links =
                    [
                        new(
                            "Lua types and API reference",
                            "https://github.com/alsi-lawr/BlokeBot/blob/master/docs/plugin-authoring/v1.md#typed-lua-api"
                        ),
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Handlers", "plugin-development/handlers"),
                new SiteLink("Host API", "plugin-development/host-api"),
            ],
        };
    }
}
