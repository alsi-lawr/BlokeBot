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
        "Check all additions and updates. Check all skips and removals before you apply the import."
    );

    private static IEnumerable<SiteGuidePage> CreateConfigurationTransferPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/configuration-transfer",
            Eyebrow = "Move channel settings",
            Title = "Export and move channel settings",
            Summary =
                "Export supported channel settings to import them into another channel. The file is not a full backup.",
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
                    Bullets =
                    [
                        "Broadcasters can use Configuration transfer for the selected channel.",
                        "Administrators can use Configuration transfer for the selected channel.",
                        "Moderators can use Configuration transfer for the selected channel.",
                        "Before the import, export the supported settings from the destination channel.",
                        "Keep each export private.",
                        "Do not use an export as a full backup.",
                    ],
                    Heading = "Before you start",
                    Paragraphs =
                    [
                        "The source channel supplies the settings. The destination channel receives the settings.",
                        "BlokeBot checks saved access and current Twitch moderator status when a moderator applies an import.",
                    ],
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
                        "Check the seven selected sections.",
                        "For Overlays, check the URL layers and Media-document links selections.",
                        "If you export URL layers, check the Complete URLs can contain credentials warning.",
                        "Clear each section or Overlay child option that you do not need.",
                        "Select Download configuration.",
                        "Store the JSON file in a private location.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Automations includes core visual flows.",
                        "Automations includes graph layout.",
                        "Automations includes nodes.",
                        "Automations includes bindings.",
                        "Automations includes expressions.",
                        "Automations includes failure policies.",
                        "Automations includes aliases.",
                        "Automations includes positions.",
                        "Automations includes edges.",
                        "Automations includes host references.",
                        "Overlays includes portable core Browser Sources.",
                        "Overlays includes appearance.",
                        "Overlays includes cues.",
                        "Overlays includes queue policies.",
                        "Points & giveaways includes the point label.",
                        "Points & giveaways includes command aliases.",
                        "Points & giveaways includes replies.",
                        "Points & giveaways includes gambling rules.",
                        "Points & giveaways includes giveaway rules.",
                        "Guessing game includes profiles.",
                        "Guessing game includes canonical slugs.",
                        "Guessing game includes default states.",
                        "Guessing game includes correct-guess rewards.",
                        "Guessing game includes command aliases.",
                        "Guessing game includes replies.",
                        "Guessing game includes answers.",
                        "Guessing game includes reply targets.",
                        "Announcements item settings include enabled states.",
                        "Announcements item settings include delivery options.",
                        "Announcements item settings include colors.",
                        "Announcements item settings include retry rules.",
                        "Announcements item settings include lifetime rules.",
                        "Announcements item settings include schedules.",
                        "The Custom commands section includes reusable replies.",
                        "The Custom commands section includes counters.",
                        "The Custom commands section includes command names.",
                        "The Custom commands section includes aliases.",
                        "The Custom commands section includes access rules.",
                        "The Custom commands section includes cooldowns.",
                        "The Custom commands section includes limits.",
                        "The Custom commands section includes actions.",
                        "The Custom commands section includes routes.",
                        "The Custom commands section includes enabled states.",
                    ],
                    Heading = "Export contents",
                    LegacyAnchor = "what-the-file-contains",
                    Facts =
                    [
                        new SiteGuideFact(
                            "Custom commands",
                            "Merge by name and Replace section import the dashboard time zone."
                        ),
                        new SiteGuideFact(
                            "Announcements",
                            "Announcements includes reusable replies and item settings. Weekly schedules store UTC recurrence. An Announcements-only import keeps the destination time zone, which controls display but not UTC recurrence."
                        ),
                        new SiteGuideFact(
                            "Guessing game",
                            "The section contains reusable Guessing game configuration."
                        ),
                        new SiteGuideFact(
                            "Points & giveaways",
                            "The section contains reusable Points & giveaways configuration."
                        ),
                        new SiteGuideFact(
                            "Chat Tools enablement",
                            "Chat Tools enablement includes 20 independent feature switches. The import applies only the switch changes that you select."
                        ),
                        new SiteGuideFact(
                            "Overlays",
                            "URL layers and Media-document links are separate child selections. URL layers preserve complete URLs. Media-document links contain immutable document IDs and metadata, but no media bytes. These links work only in the same BlokeBot instance."
                        ),
                        new SiteGuideFact(
                            "Automations",
                            "Format 1 transfers safely stored invalid core flows for repair."
                        ),
                    ],
                    Note =
                        "Keep each export private. Complete Overlay URLs can contain credentials. Exports do not contain viewer identities or command viewer allow lists.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Exports replace source database keys with deterministic export-local references.",
                        "Fixed Actor and Channel identities become identity-free placeholders.",
                        "Unresolved Automation references also become identity-free placeholders for editor repair.",
                        "Overlay media uses immutable same-instance document IDs without media bytes.",
                        "Format 1 excludes community data.",
                        "Format 1 excludes Lua configuration.",
                        "Format 1 excludes plugin configuration.",
                        "Format 1 rejects plugin-defined Automation nodes.",
                        "Exports do not contain alerts.",
                        "Exports do not contain queued public chat.",
                        "Exports do not contain delivery receipts.",
                        "Exports do not contain stream runtime state.",
                        "Exports do not contain Overlay queues.",
                        "Exports do not contain Automation runs.",
                        "Exports do not contain history.",
                        "Exports do not contain point balances.",
                        "Exports do not contain point ledgers.",
                        "Exports do not contain Guessing game rounds.",
                        "Exports do not contain votes.",
                        "Exports do not contain leaderboards.",
                        "Exports do not contain giveaway entrants.",
                        "Exports do not contain draw results.",
                        "Exports do not contain OAuth tokens.",
                        "Exports do not contain client secrets.",
                        "Exports do not contain application credentials.",
                        "Exports do not contain sessions.",
                        "Exports do not contain cookies.",
                        "Exports do not contain server paths.",
                        "Exports do not contain deployment settings.",
                    ],
                    Heading = "Export boundaries",
                    Facts =
                    [
                        new SiteGuideFact(
                            "Credentials and server settings",
                            "Exports exclude credentials and server settings."
                        ),
                        new SiteGuideFact(
                            "Live channel data",
                            "Exports exclude live channel data."
                        ),
                        new SiteGuideFact(
                            "Runtime and delivery data",
                            "Format 1 also excludes unsupported feature configuration."
                        ),
                        new SiteGuideFact(
                            "Linked definitions",
                            "Exports replace database identities with portable references where supported."
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
                        "Check the Source and BlokeBot values.",
                        "Check the Format and Exported values.",
                        "Use the Format value to check compatibility.",
                        "Use the BlokeBot value only as information about the export.",
                        "Select the sections that you want to import.",
                        "Check the add and update counts.",
                        "Check the skip and remove counts.",
                        "Select a Conflict strategy for each selected section.",
                        "Resolve each required decision.",
                        "Select Apply selected sections.",
                    ],
                    Note = "File validation and review do not change the destination channel.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "An explicit Destination profile selection overrides the automatic target.",
                        "Otherwise, Guessing game uses an exact canonical slug.",
                        "If no profile matches, BlokeBot creates a profile.",
                        "BlokeBot updates a matched profile in place.",
                        "The profile keeps its ID and linked rounds.",
                        "Replace section uses Merge by name behavior.",
                        "Replace section removes eligible items that exist only in the destination.",
                        "Shared replies stay when retained commands or Announcements need them.",
                        "BlokeBot can remove an absent Guessing game profile without rounds or an absent Automation flow without runs.",
                        "For Replace section, referenced destination Overlays require Retain target item or import cancellation.",
                        "An empty imported collection can remove eligible destination-only items.",
                        "Merge by name adds new items and updates names that match without regard to letter case.",
                        "Merge by name keeps items that exist only in the destination.",
                        "Matched Overlay records and Automation flows update in place.",
                        "Matched Automation flows keep their runs and history.",
                        "For Merge by name, Points & giveaways replaces its settings record.",
                        "Add missing adds names that do not exist and skips names that match.",
                        "Add missing keeps the destination time zone.",
                        "For Guessing game, Add missing skips matched profiles.",
                        "If no Points settings exist, Add missing applies the imported record.",
                        "Otherwise, Add missing skips that record.",
                    ],
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
                            "Add missing applies only missing configuration."
                        ),
                        new SiteGuideFact(
                            "Merge by name",
                            "Merge by name applies the merge rules."
                        ),
                        new SiteGuideFact(
                            "Replace section",
                            "Replace section also removes eligible destination-only configuration."
                        ),
                        new SiteGuideFact(
                            "Guessing game profile targets",
                            "Guessing game uses the explicit selection or the canonical slug to select a target."
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
                    Bullets =
                    [
                        "After Apply selected sections, BlokeBot checks the file again.",
                        "After Apply selected sections, BlokeBot checks the destination again.",
                        "After Apply selected sections, BlokeBot checks the authority again.",
                        "After Apply selected sections, BlokeBot checks the preview again.",
                        "One serializable transaction saves selected settings and switch changes.",
                        "The same transaction saves transition boundaries.",
                        "The same transaction saves the import audit.",
                    ],
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
                            "A pending activation record exists only for a selected Chat Tools switch change that alters feature state. If the transaction fails, BlokeBot saves none of these changes."
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
                    Bullets =
                    [
                        "For recovery, review the recovery file.",
                        "Import the recovery file.",
                        "Use Replace section only for removals that the review shows.",
                        "Select each feature switch change that you must reverse.",
                        "For Feature activation failures: Select Retry activation.",
                        "For Feature activation failures: Do not import the file again.",
                        "For Feature activation failures: Give the Operation ID to support.",
                        "For import errors: Copy the Operation ID and message.",
                        "For import errors: Check the selected channel and your authority.",
                        "For import errors: Apply the import again.",
                        "For file validation errors: Copy the location and message.",
                        "For file validation errors: Correct the source file.",
                        "For file validation errors: If you cannot correct it, export the file again.",
                        "For file validation errors: Select Validate and review again.",
                    ],
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
                            "For file validation errors."
                        ),
                        new SiteGuideFact("Import rejected or not saved", "For import errors."),
                        new SiteGuideFact(
                            "Feature activation failed",
                            "For Feature activation failures."
                        ),
                        new SiteGuideFact(
                            "Unwanted settings",
                            "The recovery file does not reverse switch changes automatically."
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
                new SiteLink("Check moderator access", "moderators"),
                new SiteLink("Troubleshoot BlokeBot", "troubleshooting"),
            ],
        };
    }
}
