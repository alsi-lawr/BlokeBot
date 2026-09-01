namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginManifestPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development/manifest",
            Eyebrow = "Plugin manifest",
            Title = "plugin.toml",
            Summary =
                "plugin.toml defines the package identity, compatibility, files, settings, features, handlers, pages, automations, and migrations.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Plugin identity",
                    Paragraphs =
                    [
                        "Choose one stable plugin ID. Give the repository directory the same ID.",
                        "Use a semantic version and a mutable Git tag. Do not identify the package with a commit SHA.",
                    ],
                    Code = """
                        manifestVersion = 1
                        id = "community.my-plugin"
                        name = "My plugin"
                        description = "Adds one documented channel tool."
                        entryModule = "main"

                        [marketplace]
                        author = "Plugin author"
                        tags = ["example"]
                        mediaUrls = []

                        [release]
                        declaredVersion = "0.1.0"
                        tag = "community-my-plugin-v0.1.0"
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Compatibility",
                    Paragraphs =
                    [
                        "Declare one API range and one BlokeBot version range. Declare Lua 5.4 and every supported runtime target.",
                        "Each target for an asset or payload must also appear in the top-level target list.",
                    ],
                    Code = """
                        [compatibility]
                        minimumApiVersion = 1
                        maximumApiVersion = 1
                        minimumBlokeBotVersion = "0.13.0"
                        maximumBlokeBotVersionExclusive = "0.14.0"
                        luaVersion = "lua54"
                        supportedTargets = [
                          "linux-x64",
                          "linux-arm64",
                          "osx-arm64",
                          "win-x64",
                          "win-arm64",
                        ]
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Package files",
                    Facts =
                    [
                        new("luaModules", "Lists each Lua module and its package path."),
                        new(
                            "assets",
                            "Lists browser or media files with their media type, purpose, targets, and maximum size."
                        ),
                        new("payloads", "Lists other reviewed files and their runtime targets."),
                    ],
                    Code = """
                        [[luaModules]]
                        id = "main"
                        path = "lua/main.lua"

                        [[assets]]
                        id = "page-document"
                        path = "web/index.html"
                        kind = "browser"
                        mediaType = "text/html"
                        purpose = "Provides the plugin page."
                        runtimeIdentifiers = ["linux-x64"]
                        maximumBytes = 65536
                        """,
                    Note =
                        "The package validator rejects undeclared files, missing files, unsafe paths, links, path collisions, or incompatible targets.",
                },
                new SiteGuideSection
                {
                    Heading = "Settings and features",
                    Paragraphs =
                    [
                        "Installation settings apply to the plugin installation. Channel settings apply to one feature on one BlokeBot host.",
                        "BlokeBot protects a secret at rest. It never returns the saved value to an administrator page.",
                    ],
                    Facts =
                    [
                        new("text", "A bounded single-line string."),
                        new("multilineText", "A bounded multiline string."),
                        new("integer", "A bounded integer."),
                        new("number", "A bounded finite number."),
                        new("boolean", "A true or false value."),
                        new("choice", "One value from declared choices."),
                        new("duration", "A duration within declared second limits."),
                        new("secret", "A protected bounded string."),
                    ],
                    Code = """
                        [[settings]]
                        id = "response-message"
                        name = "Response message"
                        description = "Sets the command response."
                        scope = "channel"
                        required = false

                        [settings.schema]
                        kind = "text"
                        maximumLength = 500

                        [[features]]
                        id = "starter"
                        name = "Starter command"
                        description = "Replies to one command."
                        settings = ["response-message"]
                        automationTemplates = []
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Host modules",
                    Paragraphs =
                    [
                        "Declare every standard host module that the Lua code calls. Set the supported API range for each module.",
                    ],
                    Code = """
                        [[hostModules]]
                        id = "settings"
                        minimumVersion = 1
                        maximumVersion = 1

                        [[hostModules]]
                        id = "responses"
                        minimumVersion = 1
                        maximumVersion = 1
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Manifest validation",
                    Steps =
                    [
                        "Run blokebot-plugin generate after each manifest change.",
                        "Fix each manifest or package error.",
                        "Run blokebot-plugin validate for all supported targets.",
                        "Run blokebot-plugin test when tests.toml exists.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Lua SDK", "plugin-development/lua-sdk"),
                new SiteLink("Handlers", "plugin-development/handlers"),
            ],
        };
    }
}
