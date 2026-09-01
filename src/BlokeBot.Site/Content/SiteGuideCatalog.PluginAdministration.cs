namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginAdministrationPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/server-owners/plugins",
            Eyebrow = "Technical operations",
            Title = "Install and recover trusted plugins",
            Summary =
                "Only a BlokeBot administrator can install, update, restart, or remove a curated plugin.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/dashboard/phone-dark-admin.png",
                LightPhoneSource: "media/dashboard/phone-light-admin.png",
                DarkLaptopSource: "media/dashboard/laptop-dark-admin.png",
                LightLaptopSource: "media/dashboard/laptop-light-admin.png",
                PhoneAlt: "The Admin page with Bot account, channel creation, and channel management controls.",
                LaptopAlt: "The Admin page with Bot account, channel creation, and channel management controls.",
                "Open Admin to manage the Bot account and BlokeBot channels. The Plugins panel is lower on the same page."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Replace the old saved-script help",
                    Paragraphs =
                    [
                        "This guide replaces the old saved-script and capability help. Current plugins do not use a capability grant or security sandbox.",
                    ],
                    Note =
                        "A curated plugin is fully trusted. It has the same filesystem, process, and network access as the BlokeBot service account. Install only curated packages that you trust.",
                },
                new SiteGuideSection
                {
                    Heading = "Review the full trust boundary",
                    Paragraphs =
                    [
                        "A curated package can contain any reviewed declared payload for its supported targets. Payloads can include native, .NET, WebAssembly, browser, and media files.",
                        "Lua 5.4 is the only plugin entrypoint that BlokeBot manages. Other payloads and their dependencies remain the plugin's responsibility.",
                        "Each active plugin uses one worker process. The worker is an availability boundary, not a security sandbox.",
                    ],
                    Facts =
                    [
                        new("Linux", "linux-x64 and linux-arm64"),
                        new("macOS", "osx-arm64"),
                        new("Windows", "win-x64 and win-arm64"),
                    ],
                    Note =
                        "BlokeBot checks declarations, targets, archive paths, links, collisions, and size limits. It does not inspect trusted payload bytes by content type.",
                },
                new SiteGuideSection
                {
                    Heading = "Use the saved marketplace catalogue",
                    Facts =
                    [
                        new(
                            "Saved snapshot",
                            "Search uses the last valid catalogue snapshot and does not wait for GitHub. Offline search can use this snapshot."
                        ),
                        new(
                            "Refresh failure",
                            "Search keeps the previous snapshot. Admin shows its age and the refresh failure."
                        ),
                        new(
                            "No snapshot",
                            "The marketplace is unavailable until one refresh succeeds."
                        ),
                        new(
                            "Package download",
                            "Install and Update still need GitHub. BlokeBot does not cache package archives."
                        ),
                    ],
                    Paragraphs =
                    [
                        "An entry selects a compatible declared version and one Git tag. Plugin identity never contains a commit SHA.",
                        "The community link queue is a local reference example. Its catalogue entry and tag are not public.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Install and configure",
                    Steps =
                    [
                        "Open Admin and find Plugins.",
                        "Review the package source, declared payloads, supported targets, version, and tag.",
                        "Select Install.",
                        "Enter the generated installation settings.",
                        "Enter each required protected secret.",
                        "Save the settings before you enable channel features.",
                    ],
                    Note =
                        "BlokeBot never shows a saved secret value. Only an admitted invocation from that plugin can read its protected value.",
                },
                new SiteGuideSection
                {
                    Heading = "Update the tag",
                    Paragraphs =
                    [
                        "Update is always a manual administrator action. It downloads and validates the current package at the selected tag.",
                        "BlokeBot stops the current plugin work before it applies the update.",
                        "The old version stays active during non-durable preparation. If durable migration starts, the old code can never resume.",
                        "A validation, migration, or activation failure faults the selected installation. Correct the package or tag, then start a new administrator action.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover a worker fault",
                    Paragraphs =
                    [
                        "BlokeBot performs one automatic restart after an unexpected worker exit. A second unexpected exit faults the plugin.",
                    ],
                    Steps =
                    [
                        "Open Admin and read the latest operation message.",
                        "Correct the package, dependency, or host problem.",
                        "Select Restart.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Remove a plugin permanently",
                    Paragraphs = ["Remove permanently deletes all plugin installation state:"],
                    Bullets =
                    [
                        "Installed package files.",
                        "Installation and feature settings, configuration, and secrets.",
                        "Schedules and the private SQLite database with its sidecar files.",
                        "Plugin definitions, automation ledgers, dependent flows, and dependent nodes.",
                        "Run history, source receipts, the marketplace installation receipt, and plugin invocation context.",
                    ],
                    Note =
                        "Remove keeps only global catalogue metadata. There is no Purge or plugin-data retention. Remove leaves no unavailable page, unavailable flow, or latest lifecycle outcome.",
                },
                new SiteGuideSection
                {
                    Heading = "Use author documentation for package details",
                    Paragraphs =
                    [
                        "The operator guide does not define plugin APIs or package schemas. Use the generated author reference for those contracts.",
                    ],
                    Links =
                    [
                        new SiteLink("Plugin development wiki", "plugin-development"),
                        new SiteLink(
                            "Read the trusted plugin contract",
                            "https://github.com/alsi-lawr/BlokeBot/blob/master/docs/trusted-plugin-contract.md"
                        ),
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Configure channel plugin features", "plugins"),
                new SiteLink("Diagnose a plugin failure", "troubleshooting"),
            ],
        };
    }
}
