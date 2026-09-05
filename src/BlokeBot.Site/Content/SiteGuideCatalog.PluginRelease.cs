namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginReleasePages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development/test-publish",
            Eyebrow = "Plugin releases",
            Title = "Testing and releases",
            Summary =
                "The release process checks each target and submits the tested package to the curated repository.",
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The release process validates each target.",
                        "It runs local tests.",
                        "It tags the package.",
                        "It adds the package to the curated repository.",
                    ],
                    Heading = "Author commands",
                    Facts =
                    [
                        new(
                            "init",
                            "Creates a complete starter project in a new destination. It does not overwrite existing files."
                        ),
                        new(
                            "generate",
                            "Validates plugin.toml and replaces only generated SDK files that contain markers."
                        ),
                        new(
                            "validate",
                            "Validates the manifest and package for all supported runtime targets."
                        ),
                        new(
                            "test",
                            "Runs tests.toml scenarios through the plugin worker for the current runtime target."
                        ),
                    ],
                    Code = """
                        blokebot-plugin generate .
                        blokebot-plugin validate .
                        blokebot-plugin test .
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "tests.toml",
                    Paragraphs =
                    [
                        "tests.toml defines local test scenarios. The validated plugin package does not include this file.",
                        "The harness gives deterministic results for host calls. It does not install the plugin. It does not contact Twitch or third-party services.",
                    ],
                    Code = """
                        name = "community.my-plugin"

                        [[scenarios]]
                        name = "starter-command"
                        workerMode = "admitted"
                        invocationKind = "command"
                        module = "main"
                        operation = "handle_command"
                        expectation = "returned"
                        input = { route = "plugin-starter", arguments = [] }
                        expectedHostCalls = ["settings.feature", "responses.chat"]
                        """,
                    Bullets =
                    [
                        "Add one scenario for each published handler path.",
                        "List the expected host calls in their execution order.",
                        "Add a migrationFailed scenario for an update failure fixture.",
                        "Add a workerExited scenario for an intentional crash fixture.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Repository layout",
                    Paragraphs =
                    [
                        "BlokeBot reads marketplace entries from each plugin.toml in the curated repository. The repository has no separate catalog file.",
                        "Place the package at plugins/<plugin-id>/. The directory name must equal the ID in plugin.toml.",
                    ],
                    Code = """
                        plugins/
                          community.my-plugin/
                            plugin.toml
                            lua/
                              main.lua
                            .blokebot/
                            .luarc.json
                            tests.toml
                        """,
                    Links =
                    [
                        new(
                            "Curated plugin repository",
                            "https://github.com/alsi-lawr/blokebot-plugins"
                        ),
                        new(
                            "Community link queue example",
                            "https://github.com/alsi-lawr/blokebot-plugins/tree/master/plugins/community.link-queue"
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "For each download, BlokeBot records the version.",
                        "For each download, BlokeBot records the tag.",
                        "For each download, BlokeBot records a unique identity.",
                    ],
                    Heading = "Release identity",
                    Steps =
                    [
                        "Set declaredVersion to the release semantic version.",
                        "Set tag to the Git tag for that release.",
                        "Check that the package repository resolves the tag.",
                        "Run generate from the final package tree.",
                        "Run validate from the final package tree.",
                        "Run test from the final package tree.",
                        "Submit the package through the curated repository review process.",
                    ],
                    Note = "Do not put a commit SHA in plugin.toml.",
                },
                new SiteGuideSection
                {
                    Heading = "Updates",
                    Paragraphs =
                    [
                        "An administrator starts each update. BlokeBot downloads the current tag before it replaces the plugin.",
                        "A moved tag with the same version still creates a new download and worker.",
                        "If a tag is missing or a package is invalid, BlokeBot records the error for the administrator.",
                        "If migration or activation fails, BlokeBot also records the error.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Release checklist",
                    Bullets =
                    [
                        "Check that the package contains plugin.toml and every declared file.",
                        "Check that the generated SDK matches the final manifest.",
                        "Check that validate accepts every declared runtime target.",
                        "Check that test passes every declared scenario on the current runtime target.",
                        "Check that the tag resolves to the reviewed package tree.",
                        "Check that the package contains no protected value or token.",
                        "Check that the package contains no production endpoint.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Troubleshooting", "plugin-development/troubleshooting"),
                new SiteLink("Plugin administration", "server-owners/plugins"),
            ],
        };
    }
}
