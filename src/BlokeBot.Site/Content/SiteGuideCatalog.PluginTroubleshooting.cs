namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginTroubleshootingPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development/troubleshooting",
            Eyebrow = "Plugin development",
            Title = "Troubleshooting",
            Summary = "Author command results and runtime diagnostics identify plugin errors.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Exit codes",
                    Facts =
                    [
                        new("0", "The command completed."),
                        new("2", "The command has invalid syntax."),
                        new("3", "The local source or tests.toml source is invalid."),
                        new("4", "The manifest or package validation failed."),
                        new("5", "The worker for the current runtime is unavailable."),
                        new("6", "One or more test scenarios failed."),
                        new("7", "The output write failed."),
                        new("8", "The project operation was rejected."),
                        new("130", "The author operation was canceled."),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manifest and package errors",
                    Steps =
                    [
                        "Read the first reported path and error code.",
                        "Fix the field or file at that path.",
                        "Run blokebot-plugin generate again.",
                        "Run blokebot-plugin validate again.",
                    ],
                    Bullets =
                    [
                        "Check the exact TOML field name and value kind.",
                        "Check each declared identifier and reference.",
                        "Check each package path and letter case.",
                        "Check each asset and payload.",
                        "Check each top-level target.",
                        "Check that no link or undeclared file exists in the package.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "LuaLS",
                    Steps =
                    [
                        "Run blokebot-plugin generate from the plugin root.",
                        "Verify that .luarc.json lists ./.blokebot/lua/5.4/v1.",
                        "Restart the Lua language server for the workspace.",
                        "If generation rejects the project, fix each plugin.toml error.",
                    ],
                    Note =
                        "Do not edit the generated SDK to hide an error. The next generation replaces that directory.",
                },
                new SiteGuideSection
                {
                    Heading = "Host call errors",
                    Bullets =
                    [
                        "Declare the host module and supported API range in plugin.toml.",
                        "Call the function only from a supported context.",
                        "Use the generated parameter types and limits.",
                        "Use context.current. Do not supply a host or feature identity.",
                        "Use the typed failure code and safe message as the result.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Page action errors",
                    Bullets =
                    [
                        "Set the action kind to page.",
                        "Match the field IDs in the generated form.",
                        "Match the required flags and value kinds in the generated form.",
                        "Send only the declared fields.",
                        "Use a new message ID for each embedded page action attempt.",
                        "If the session expires or the plugin generation changes, reload the page.",
                    ],
                    Note =
                        "HTTP actions do not run through pages. Page actions do not run through the fixed HTTP action endpoint.",
                },
                new SiteGuideSection
                {
                    Heading = "Runtime states",
                    Facts =
                    [
                        new(
                            "Disabled",
                            "Enable the feature after its required settings and template graph are valid."
                        ),
                        new(
                            "EnabledDegraded",
                            "Check the declared Twitch scopes and EventSub readiness for work that requires Twitch."
                        ),
                        new(
                            "WorkerExited",
                            "Read the worker diagnostic. One automatic recovery attempt can run before the installation enters a fault state."
                        ),
                        new(
                            "MigrationFailed",
                            "Correct the selected package or migration and start a new administrator action. Old code does not resume."
                        ),
                        new(
                            "RecoveryPackageUnavailable",
                            "Restore the exact selected package through an administrator install or update action."
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Diagnostic evidence",
                    Bullets =
                    [
                        "Record the author command and its exit code.",
                        "Record the manifest path and validation error code.",
                        "Record the installation state and administrator action.",
                        "Record the safe failure code.",
                        "Record the feature readiness and required Twitch state.",
                        "Remove protected values and tokens from the evidence.",
                        "Remove request bodies and private database contents from the evidence.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Plugin development", "plugin-development"),
                new SiteLink("Administrator troubleshooting", "server-owners/plugins"),
            ],
        };
    }
}
