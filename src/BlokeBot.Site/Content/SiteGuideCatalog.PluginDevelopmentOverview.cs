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
                "A plugin project declares its access to BlokeBot. It includes generated types and the files for local tests and the curated repository.",
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The project includes generated Lua types.",
                        "The project includes declared access to BlokeBot.",
                        "The project includes local tests.",
                        "The project includes the files for the curated repository.",
                    ],
                    Heading = "Project setup",
                    Steps =
                    [
                        "Install the author tool for your BlokeBot release.",
                        "Run the project command.",
                        "Edit plugin.toml.",
                        "Run the generator after each manifest change.",
                        "Implement the declared handlers.",
                        "Validate the package.",
                        "Test the package.",
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
                        "Do not put protected values in logs.",
                        "Do not put protected values in responses.",
                        "Do not put protected values in page documents.",
                        "If BlokeBot cancels an invocation, stop that invocation.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Installation validates the package.",
                        "Installation prepares its worker.",
                        "Installation runs its migrations.",
                        "Installation activates the selected release.",
                    ],
                    Heading = "Plugin lifecycle",
                    LegacyAnchor = "installation-updates-and-removal",
                    Facts =
                    [
                        new("Install", "BlokeBot completes the installation phases in this order."),
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
                    Bullets =
                    [
                        "The generated reference lists all contract types.",
                        "The generated reference lists all fields.",
                        "The generated reference lists all outcomes.",
                        "The generated reference lists all host functions.",
                    ],
                    Heading = "API reference",
                    Paragraphs = ["This wiki describes the plugin workflow."],
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
