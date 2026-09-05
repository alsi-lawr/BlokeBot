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
            Summary = "Administer curated plugins.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/dashboard/phone-dark-admin.png",
                LightPhoneSource: "media/dashboard/phone-light-admin.png",
                DarkLaptopSource: "media/dashboard/laptop-dark-admin.png",
                LightLaptopSource: "media/dashboard/laptop-light-admin.png",
                PhoneAlt: "The Admin page shows Bot account controls. Channel creation and channel management controls are visible.",
                LaptopAlt: "The Admin page shows Bot account controls. Channel creation and channel management controls are visible.",
                "Open Admin to manage the Bot account and BlokeBot channels. The Plugins panel is lower on the same page."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "A curated plugin has the same filesystem access as the BlokeBot service account.",
                        "A curated plugin has the same process access as the BlokeBot service account.",
                        "A curated plugin has the same network access as the BlokeBot service account.",
                        "Only a BlokeBot administrator can install a curated plugin.",
                        "Only a BlokeBot administrator can update a curated plugin.",
                        "Only a BlokeBot administrator can restart a curated plugin.",
                        "Only a BlokeBot administrator can remove a curated plugin.",
                    ],
                    Heading = "Trusted plugins",
                    LegacyAnchor = "replace-the-old-saved-script-help",
                    Paragraphs =
                    [
                        "Current plugins do not use a capability grant or security sandbox.",
                    ],
                    Note =
                        "Install only curated packages that you trust. A curated plugin is fully trusted.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "BlokeBot checks declarations.",
                        "BlokeBot checks targets.",
                        "BlokeBot checks archive paths.",
                        "BlokeBot checks links.",
                        "BlokeBot checks collisions.",
                        "BlokeBot checks size limits.",
                        "Native files.",
                        ".NET files.",
                        "WebAssembly files.",
                        "Browser files.",
                        "Media files.",
                    ],
                    Heading = "Plugin permissions",
                    LegacyAnchor = "review-the-full-trust-boundary",
                    Paragraphs =
                    [
                        "A curated package can contain any reviewed declared payload for its supported targets. Supported payload types appear below.",
                        "Lua 5.4 is the only plugin entrypoint that BlokeBot manages. Other payloads and their dependencies remain the plugin's responsibility.",
                        "Each active plugin uses one worker process. The worker is an availability boundary, not a security sandbox.",
                    ],
                    Facts =
                    [
                        new("Linux", "linux-x64 and linux-arm64"),
                        new("macOS", "osx-arm64"),
                        new("Windows", "win-x64 and win-arm64"),
                    ],
                    Note = "It does not inspect trusted payload bytes by content type.",
                },
                new SiteGuideSection
                {
                    Heading = "Marketplace catalog",
                    LegacyAnchor = "use-the-saved-marketplace-catalogue",
                    Facts =
                    [
                        new(
                            "Saved snapshot",
                            "Search uses the last valid catalog snapshot and does not wait for GitHub. Offline search can use this snapshot."
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
                        "The community link queue is a local reference example. Its catalog entry and tag are not public.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Install and configure",
                    Steps =
                    [
                        "Open Admin.",
                        "Find Plugins.",
                        "Review the package source.",
                        "Review the declared payloads.",
                        "Review the supported targets.",
                        "Review the version and tag.",
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
                    Bullets =
                    [
                        "A validation failure faults the selected installation.",
                        "A migration failure faults the selected installation.",
                        "An activation failure faults the selected installation.",
                    ],
                    Heading = "Update the tag",
                    Paragraphs =
                    [
                        "Update is always a manual administrator action. It downloads and validates the current package at the selected tag.",
                        "BlokeBot stops the current plugin work before it applies the update.",
                        "The old version stays active during non-durable preparation. If durable migration starts, the old code can never resume.",
                        "Correct the package or tag. Then start a new administrator action.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The problem can be in the package.",
                        "The problem can be in a dependency.",
                        "The problem can be in the host.",
                    ],
                    Heading = "Recover a worker fault",
                    Paragraphs =
                    [
                        "BlokeBot performs one automatic restart after an unexpected worker exit. A second unexpected exit faults the plugin.",
                    ],
                    Steps =
                    [
                        "Open Admin.",
                        "Read the latest operation message.",
                        "Correct the reported problem.",
                        "Select Restart.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Remove a plugin permanently",
                    Paragraphs = ["Remove permanently deletes all plugin installation state:"],
                    Bullets =
                    [
                        "Remove leaves no unavailable page.",
                        "Remove leaves no unavailable flow.",
                        "Remove leaves no latest lifecycle outcome.",
                        "Installed package files.",
                        "Installation settings.",
                        "Feature settings.",
                        "Configuration.",
                        "secrets.",
                        "Schedules and the private SQLite database with its sidecar files.",
                        "Plugin definitions and automation ledgers.",
                        "dependent flows and dependent nodes.",
                        "Run history and source receipts.",
                        "the marketplace installation receipt and plugin invocation context.",
                    ],
                    Note =
                        "Remove keeps only global catalog metadata. There is no Purge or plugin-data retention.",
                },
                new SiteGuideSection
                {
                    Heading = "Plugin package reference",
                    LegacyAnchor = "use-author-documentation-for-package-details",
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
