namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static readonly SiteMedia _configurationTransferReviewMedia = new(
        DarkPhoneSource: "media/configuration-transfer/phone-dark-review.png",
        LightPhoneSource: "media/configuration-transfer/phone-light-review.png",
        DarkLaptopSource: "media/configuration-transfer/laptop-dark-review.png",
        LightLaptopSource: "media/configuration-transfer/laptop-light-review.png",
        PhoneAlt: "The review for a Sample Channel import on a narrow screen.",
        LaptopAlt: "The review for a Sample Channel import on a wide screen.",
        "Verify all additions and updates. Verify all skips and removals before you apply the import."
    );

    private static IEnumerable<SiteGuidePage> CreateConfigurationTransferPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/configuration-transfer",
            Eyebrow = "Move channel settings",
            Title = "Export and move channel settings",
            Summary =
                "This guide explains how to export supported channel settings and import them into another channel. The file is not a full backup.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/configuration-transfer/phone-dark-export.png",
                LightPhoneSource: "media/configuration-transfer/phone-light-export.png",
                DarkLaptopSource: "media/configuration-transfer/laptop-dark-export.png",
                LightLaptopSource: "media/configuration-transfer/laptop-light-export.png",
                PhoneAlt: "The selected Overlay options and the seven-section export status on a narrow screen.",
                LaptopAlt: "The selected Overlay options and the seven-section export status on a wide screen.",
                "Select all sections and Overlay options that you want in the export."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Before you start",
                    Paragraphs =
                    [
                        "The source channel supplies the settings. The destination channel receives the settings.",
                        "Broadcasters and administrators can use Configuration transfer for the selected channel. Moderators can also use it.",
                        "BlokeBot verifies saved access and current Twitch moderator status when a moderator applies an import.",
                    ],
                    Note =
                        "Before the import, export the supported settings from the destination channel. Keep each export private. Do not use an export as a full backup.",
                },
                new SiteGuideSection
                {
                    Heading = "Export a file",
                    Paragraphs =
                    [
                        "BlokeBot selects all seven sections and both Overlay child options by default.",
                    ],
                    Steps =
                    [
                        "Open Configuration transfer for the source channel.",
                        "Verify the seven selected sections.",
                        "For Overlays, verify the URL layers and Media-document links selections.",
                        "If you export URL layers, verify the Complete URLs can contain credentials warning.",
                        "Clear each section or Overlay child option that you do not need.",
                        "Select Download configuration.",
                        "Store the JSON file in a private location.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "What the file contains",
                    Facts =
                    [
                        new SiteGuideFact(
                            "Custom commands",
                            "The Custom commands section includes reusable replies and counters. It includes command names and aliases. It includes access rules and cooldowns. It includes limits and actions. It includes routes and enabled states. Merge by name and Replace section import the dashboard time zone."
                        ),
                        new SiteGuideFact(
                            "Announcements",
                            "Announcements includes reusable replies and item settings. Item settings include enabled states and delivery options. They include colors and retry rules. They include lifetime rules and schedules. Weekly schedules store UTC recurrence. An Announcements-only import keeps the destination time zone, which controls display but not UTC recurrence."
                        ),
                        new SiteGuideFact(
                            "Guessing game",
                            "Guessing game includes profiles and canonical slugs. It includes default states and correct-guess rewards. It includes command aliases and replies. It includes answers and reply targets."
                        ),
                        new SiteGuideFact(
                            "Points & giveaways",
                            "Points & giveaways includes the point label and command aliases. It includes replies and gambling rules. It also includes giveaway rules."
                        ),
                        new SiteGuideFact(
                            "Chat Tools enablement",
                            "Chat Tools enablement includes 20 independent feature switches. The import applies only the switch changes that you select."
                        ),
                        new SiteGuideFact(
                            "Overlays",
                            "Overlays includes portable core Browser Sources and appearance. It includes cues and queue policies. URL layers and Media-document links are separate child selections. URL layers preserve complete URLs. Media-document links contain immutable document IDs and metadata, but no media bytes. These links work only in the same BlokeBot instance."
                        ),
                        new SiteGuideFact(
                            "Automations",
                            "Automations includes core visual flows and graph layout. It includes nodes and bindings. It includes expressions and failure policies. It includes aliases and positions. It includes edges and host references. Format 1 transfers safely stored invalid core flows for repair."
                        ),
                    ],
                    Note =
                        "Keep each export private. Complete Overlay URLs can contain credentials. Exports do not contain viewer identities or command viewer allow lists.",
                },
                new SiteGuideSection
                {
                    Heading = "Export boundaries",
                    Facts =
                    [
                        new SiteGuideFact(
                            "Credentials and server settings",
                            "Exports do not contain OAuth tokens or client secrets. They do not contain application credentials or sessions. They do not contain cookies or server paths. They do not contain deployment settings."
                        ),
                        new SiteGuideFact(
                            "Live channel data",
                            "Exports do not contain point balances or point ledgers. They do not contain Guessing game rounds or votes. They do not contain leaderboards or giveaway entrants. They do not contain draw results."
                        ),
                        new SiteGuideFact(
                            "Runtime and delivery data",
                            "Exports do not contain alerts or queued public chat. They do not contain delivery receipts or stream runtime state. They do not contain Overlay queues. They do not contain Automation runs or history. Format 1 excludes community data and Lua configuration. It excludes plugin configuration and rejects plugin-defined Automation nodes."
                        ),
                        new SiteGuideFact(
                            "Linked definitions",
                            "Exports replace source database keys with deterministic export-local references. Fixed Actor and Channel identities become identity-free placeholders. Unresolved Automation references also become identity-free placeholders for editor repair. Overlay media uses immutable same-instance document IDs without media bytes."
                        ),
                        new SiteGuideFact(
                            "Format and limits",
                            "BlokeBot exports UTF-8 JSON with the identifier blokebot.channel-configuration and format version 1. The maximum file size is 2 MB, and each collection accepts up to 1,000 records. The envelope and typed section records reject unknown properties and enum values. Present sections with empty collections are valid. BlokeBot accepts format 1 and adapts format 0. No other format version is valid."
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Import a file",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-upload.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-upload.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-upload.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-upload.png",
                        PhoneAlt: "The file controls for a Sample Channel import on a narrow screen.",
                        LaptopAlt: "The file controls for a Sample Channel import on a wide screen.",
                        "Upload one BlokeBot JSON export or paste its contents. Select Validate and review."
                    ),
                    MediaAfterContent = _configurationTransferReviewMedia,
                    Steps =
                    [
                        "Open Configuration transfer for the destination channel.",
                        "Select Import.",
                        "Upload the JSON file or paste its contents.",
                        "Select Validate and review.",
                        "Verify the Source and BlokeBot values.",
                        "Verify the Format and Exported values.",
                        "Use the Format value to verify compatibility.",
                        "Use the BlokeBot value only as information about the export.",
                        "Select the sections that you want to import.",
                        "Verify the add and update counts.",
                        "Verify the skip and remove counts.",
                        "Select a Conflict strategy for each selected section.",
                        "Resolve each required decision.",
                        "Select Apply selected sections.",
                    ],
                    Note = "File validation and review do not change the destination channel.",
                },
                new SiteGuideSection
                {
                    Heading = "Resolve conflicts",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-conflict.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-conflict.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-conflict.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-conflict.png",
                        PhoneAlt: "Required import decisions for Sample Channel on a narrow screen.",
                        LaptopAlt: "Required import decisions for Sample Channel on a wide screen.",
                        "Resolve each section strategy and alias decision. Resolve each dependency and Guessing game profile decision."
                    ),
                    Paragraphs =
                    [
                        "Each conflict identifies one decision that blocks the import. Only conflicts in selected sections block the import.",
                    ],
                    Facts =
                    [
                        new SiteGuideFact(
                            "Add missing",
                            "Add missing adds names that do not exist and skips names that match. It keeps the destination time zone. For Guessing game, it skips matched profiles. If no Points settings exist, it applies the imported record. Otherwise, it skips that record."
                        ),
                        new SiteGuideFact(
                            "Merge by name",
                            "Merge by name adds new items and updates names that match without regard to letter case. It keeps items that exist only in the destination. Matched Overlay records and Automation flows update in place. Matched Automation flows keep their runs and history. Points & giveaways replaces its settings record."
                        ),
                        new SiteGuideFact(
                            "Replace section",
                            "Replace section uses Merge by name behavior. It removes eligible items that exist only in the destination. Shared replies stay when retained commands or Announcements need them. BlokeBot can remove an absent Guessing game profile without rounds or an absent Automation flow without runs. Referenced destination Overlays require Retain target item or import cancellation. An empty imported collection can remove eligible destination-only items."
                        ),
                        new SiteGuideFact(
                            "Guessing game profile targets",
                            "An explicit Destination profile selection overrides the automatic target. Otherwise, Guessing game uses an exact canonical slug. If no profile matches, BlokeBot creates a profile. BlokeBot updates a matched profile in place. The profile keeps its ID and linked rounds."
                        ),
                        new SiteGuideFact(
                            "Chat Tools enablement",
                            "Chat Tools enablement does not use a Conflict strategy. The review shows a separate On or Off change for each switch."
                        ),
                        new SiteGuideFact(
                            "Aliases and dependencies",
                            "For an alias conflict, select Rename imported alias or Skip whole item. For a command dependency conflict, select Skip whole item."
                        ),
                    ],
                    Note =
                        "For Replace section, retain each absent Guessing game profile that has rounds. Also retain each absent Automation flow that has runs.",
                },
                new SiteGuideSection
                {
                    Heading = "Apply the import",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-success.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-success.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-success.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-success.png",
                        PhoneAlt: "A successful Sample Channel import on a narrow screen.",
                        LaptopAlt: "A successful Sample Channel import on a wide screen.",
                        "A successful import shows the changed sections and the Operation ID. It also shows links to the destination settings."
                    ),
                    Facts =
                    [
                        new SiteGuideFact(
                            "Database save",
                            "When you select Apply selected sections, BlokeBot verifies the file and destination again. It verifies authority and the preview again. One serializable transaction saves selected settings and switch changes. It saves transition boundaries and the import audit. A pending activation record exists only when the import selects a Chat Tools switch change that alters feature state. If the transaction fails, BlokeBot saves none of these changes."
                        ),
                        new SiteGuideFact(
                            "Feature activation",
                            "Imported configuration does not turn features on. Only selected Chat Tools enablement changes alter feature state. After the commit, a separate task updates lifecycle services. If this task fails, the imported settings and switch changes stay saved."
                        ),
                        new SiteGuideFact(
                            "Replay prevention",
                            "An import does not run a general catch-up. BlokeBot does not replay work from a disabled period. Switch changes can move transition boundaries for affected features."
                        ),
                        new SiteGuideFact(
                            "Post-commit work",
                            "Overlay refresh and Automation EventSub reconciliation start only after the database commit. If this work fails, the imported configuration stays saved. BlokeBot reports the failure separately."
                        ),
                    ],
                    Note =
                        "If Feature activation fails, select Retry activation. Do not import the file again.",
                },
                new SiteGuideSection
                {
                    Heading = "Troubleshooting",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-failed.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-failed.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-failed.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-failed.png",
                        PhoneAlt: "A failed activation task after a saved Sample Channel import on a narrow screen.",
                        LaptopAlt: "A failed activation task after a saved Sample Channel import on a wide screen.",
                        "If Feature activation fails after the save, select Retry activation. Do not import the file again."
                    ),
                    Paragraphs =
                    [
                        "File and validation errors do not change the destination. Some errors do not have an Operation ID.",
                        "A rejected import or save failure does not change the destination. An activation failure occurs after BlokeBot saves the import.",
                    ],
                    Facts =
                    [
                        new SiteGuideFact(
                            "File error or unsupported format",
                            "Copy the location and message. Correct the source file. If you cannot correct it, export the file again. Select Validate and review again."
                        ),
                        new SiteGuideFact(
                            "Import rejected or not saved",
                            "Copy the Operation ID and message. Verify the selected channel and your authority. Apply the import again."
                        ),
                        new SiteGuideFact(
                            "Feature activation failed",
                            "Select Retry activation. Do not import the file again. Give the Operation ID to support."
                        ),
                        new SiteGuideFact(
                            "Unwanted settings",
                            "Review the recovery file. Import the recovery file. Use Replace section only for removals that the review shows. Select each feature switch change that you must reverse. The recovery file does not reverse switch changes automatically."
                        ),
                        new SiteGuideFact(
                            "Absent dependency",
                            "For an absent Overlay or cue in a Custom command, select Skip whole item. In the Automation editor, select a local dependency for each identity-free placeholder."
                        ),
                        new SiteGuideFact(
                            "Retained history",
                            "For an absent Guessing game profile with rounds, select Retain target item. For an absent Automation flow with runs, select Retain target item."
                        ),
                    ],
                    Note =
                        "Do not edit export-local IDs or their references. Do not give the configuration file to support. The recovery file cannot restore excluded runtime data or history.",
                },
            ],
            Next =
            [
                new SiteLink("Manage channel tools", "tools"),
                new SiteLink("Verify moderator access", "moderators"),
                new SiteLink("Troubleshoot BlokeBot", "troubleshooting"),
            ],
        };
    }
}
