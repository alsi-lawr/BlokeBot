namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginDevelopmentOverviewPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development",
            Eyebrow = "Plugin development",
            Title = "Lua plugins",
            Summary =
                "A plugin project includes generated Lua types, declared access to BlokeBot, local tests, and the files for the curated repository.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Project setup",
                    Steps =
                    [
                        "Install the author tool for your BlokeBot release.",
                        "Run the project command.",
                        "Edit plugin.toml.",
                        "Run the generator after each manifest change.",
                        "Implement the declared handlers.",
                        "Validate and test the package.",
                    ],
                    Code = """
                        blokebot-plugin init community.my-plugin ./my-plugin
                        cd ./my-plugin
                        blokebot-plugin generate .
                        blokebot-plugin validate .
                        blokebot-plugin test .
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Project files",
                    Facts =
                    [
                        new("plugin.toml", "Defines the package and its access to BlokeBot."),
                        new("lua/", "Contains the Lua modules that you write."),
                        new(
                            ".blokebot/lua/5.4/v1/",
                            "Contains the generated LuaLS types and handler skeletons."
                        ),
                        new(".luarc.json", "Adds the generated SDK to your LuaLS workspace."),
                        new(
                            "tests.toml",
                            "Defines optional scenarios for the deterministic local test runner."
                        ),
                    ],
                    Note =
                        "Do not edit generated files. Copy a handler skeleton into an author-owned Lua module.",
                },
                new SiteGuideSection
                {
                    Heading = "Permissions and security",
                    Paragraphs =
                    [
                        "BlokeBot fully trusts each curated plugin. The worker protects BlokeBot from plugin crashes and resource failures. It is not a security sandbox.",
                        "Lua 5.4 is the managed entry point. A package can also declare reviewed files for supported targets.",
                    ],
                    Bullets =
                    [
                        "Declare every host module that your plugin uses.",
                        "Use only the plugin and feature identities that BlokeBot supplies for the invocation.",
                        "Do not put protected values in logs, responses, or page documents.",
                        "If BlokeBot cancels an invocation, stop that invocation.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Installation, updates, and removal",
                    Facts =
                    [
                        new(
                            "Install",
                            "BlokeBot validates the package, prepares its worker, runs its migrations, and activates the selected release."
                        ),
                        new(
                            "Update",
                            "For each manual update, BlokeBot downloads the current tag again before it applies the update."
                        ),
                        new(
                            "Installation fault",
                            "A migration or activation failure faults the selected installation. After a durable migration starts, BlokeBot does not resume the old code."
                        ),
                        new(
                            "Remove",
                            "Removal deletes the package and all state that belongs to the plugin. BlokeBot does not provide a retention mode or a separate purge mode."
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "API reference",
                    Paragraphs =
                    [
                        "This wiki explains the plugin workflow. The generated reference lists every contract type, field, outcome, and host function.",
                    ],
                    Links =
                    [
                        new(
                            "Generated v1 API reference",
                            "https://github.com/alsi-lawr/BlokeBot/blob/master/docs/plugin-authoring/v1.md"
                        ),
                        new(
                            "Trusted plugin contract",
                            "https://github.com/alsi-lawr/BlokeBot/blob/master/docs/trusted-plugin-contract.md"
                        ),
                    ],
                },
            ],
            Next =
            [
                new SiteLink("plugin.toml", "plugin-development/manifest"),
                new SiteLink("Lua SDK", "plugin-development/lua-sdk"),
            ],
        };
    }
}
