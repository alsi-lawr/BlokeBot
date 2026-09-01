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
            Summary =
                "The generated module gives LuaLS type information for parameters, results, failures, context, settings, and handler input.",
            Sections =
            [
                new SiteGuideSection
                {
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
                    Heading = "Type generation",
                    Paragraphs =
                    [
                        "The generator reads plugin.toml and creates SDK types, plugin settings, handler types, and executable no-op skeletons.",
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
                    Heading = "Generated type definitions",
                    Facts =
                    [
                        new(
                            "BlokeBotContext",
                            "A union of installation, channel, automation, migration, and page contexts."
                        ),
                        new(
                            "BlokeBotInstallationSettings",
                            "The installation settings declared in plugin.toml for the current plugin."
                        ),
                        new(
                            "BlokeBotFeatureSettings",
                            "The feature settings declared in plugin.toml for the current host feature."
                        ),
                        new(
                            "BlokeBotHostFailure",
                            "A stable failure kind, code, and safe message."
                        ),
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
                    Heading = "Context and settings",
                    Paragraphs =
                    [
                        "context.current returns the identity for the current call. Plugin code cannot choose a different plugin, host, feature, or actor.",
                        "settings.installation and settings.feature return declared values for the current plugin installation or host feature. Unset optional values are absent.",
                    ],
                    Code = """
                        local context = blokebot.context.current()
                        local settings = blokebot.settings.feature()

                        local message = settings["response-message"] or "Hello."
                        blokebot.responses.chat(message)
                        """,
                    Note =
                        "Do not expose a protected setting in logs, diagnostics, responses, audit fields, failure messages, or page documents.",
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
