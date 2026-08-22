namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static readonly SiteMedia _configurationTransferReviewMedia = new(
        DarkPhoneSource: "media/configuration-transfer/phone-dark-review.png",
        LightPhoneSource: "media/configuration-transfer/phone-light-review.png",
        DarkLaptopSource: "media/configuration-transfer/laptop-dark-review.png",
        LightLaptopSource: "media/configuration-transfer/laptop-light-review.png",
        PhoneAlt: "The import preview for Sample Channel on a narrow screen.",
        LaptopAlt: "The import preview for Sample Channel on a wide screen.",
        "Check all additions, updates, skips, and removals before you save the import."
    );

    private static IEnumerable<SiteGuidePage> CreateConfigurationTransferPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/configuration-transfer",
            Eyebrow = "Move channel settings",
            Title = "Export and move channel settings",
            Summary =
                "This guide explains how to export supported settings from one channel and apply them to another channel. The file is not a full backup.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/configuration-transfer/phone-dark-export.png",
                LightPhoneSource: "media/configuration-transfer/phone-light-export.png",
                DarkLaptopSource: "media/configuration-transfer/laptop-dark-export.png",
                LightLaptopSource: "media/configuration-transfer/laptop-light-export.png",
                PhoneAlt: "The export section list for Sample Channel on a narrow screen.",
                LaptopAlt: "The export section list for Sample Channel on a wide screen.",
                "Select the source channel. Select the sections that you want to export."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Before you start",
                    Paragraphs =
                    [
                        "The source channel provides the settings. The destination channel receives the settings.",
                        "The broadcaster, an administrator, or a moderator can use configuration transfer for the selected channel.",
                        "When a moderator applies an import, BlokeBot checks saved access and current Twitch moderator status.",
                    ],
                    Note =
                        "Before the import, export the supported settings from the destination channel. Keep this recovery file and every other export private. Do not use the recovery file as a full backup.",
                },
                new SiteGuideSection
                {
                    Heading = "Export a file",
                    Paragraphs = ["BlokeBot selects all seven sections by default."],
                    Steps =
                    [
                        "Open Configuration transfer for the source channel.",
                        "Check the seven selected sections.",
                        "Clear each section that you do not need.",
                        "Download the JSON file.",
                        "Store the file in a private location.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "What the file contains",
                    Facts =
                    [
                        new SiteGuideFact(
                            "Custom commands",
                            "The section includes the dashboard time zone, reusable replies, and counter names and values. It includes command names, aliases, general access rules, cooldowns, limits, actions, and routes. It also records whether each command is enabled. Viewer allow lists stay local to the destination channel."
                        ),
                        new SiteGuideFact(
                            "Announcements",
                            "The section includes replies, enabled status, delivery, color, retry rules, lifetime rules, and schedules. Weekly schedules use a UTC weekday and time. An import of only Announcements keeps the destination time zone. Daylight saving time can change the displayed local time."
                        ),
                        new SiteGuideFact(
                            "Time zones",
                            "The Custom commands section contains the dashboard time zone. With Merge or Replace, BlokeBot imports this time zone. With Add missing, BlokeBot keeps the destination time zone. A change to the editor display does not change the stored UTC schedule."
                        ),
                        new SiteGuideFact(
                            "Guessing",
                            "The section includes profiles, canonical slugs, default status, rewards for correct guesses, command aliases, replies, answers, and reply targets."
                        ),
                        new SiteGuideFact(
                            "Points & giveaways",
                            "The section includes the point label, command aliases, replies, gambling rules, and giveaway rules."
                        ),
                        new SiteGuideFact(
                            "Chat Tools enablement",
                            "The section includes twenty independent feature switches. The import applies only the switch changes that you select."
                        ),
                        new SiteGuideFact(
                            "Overlays",
                            "The section includes portable core Browser Sources, appearance, cues, and queue policies. Select URL layers and media-document links independently. Complete URLs can contain credentials in query strings. Media links contain same-instance document IDs, never media bytes."
                        ),
                        new SiteGuideFact(
                            "Automations",
                            "The section includes core visual flow definitions, graph layout, nodes, bindings, expressions, policies, aliases, positions, and edges. Safely stored invalid core flows also transfer for repair. Fixed Actor and Channel identities and unresolved local references become invalid identity-free placeholders. Runs and history do not enter the file."
                        ),
                    ],
                    Note =
                        "Keep every export private. Complete Overlay URLs can contain credentials. Configuration transfer does not include viewer IDs, logins, display names, or command viewer allow lists.",
                },
                new SiteGuideSection
                {
                    Heading = "Export boundaries",
                    Facts =
                    [
                        new SiteGuideFact(
                            "Credentials and server settings",
                            "Exports do not contain credentials, sessions, cookies, server settings, or deployment settings."
                        ),
                        new SiteGuideFact(
                            "Live channel data",
                            "Exports do not contain point balances, point ledgers, Guessing rounds, votes, leaderboards, giveaway entrants, or draw results."
                        ),
                        new SiteGuideFact(
                            "Runtime and delivery data",
                            "Exports do not contain durable alerts, queued public chat, delivery receipts, or runtime state for streams and community features."
                        ),
                        new SiteGuideFact(
                            "Linked definitions",
                            "Exports replace source database keys with deterministic local references. An Automation reference that cannot be mapped becomes an identity-free placeholder for repair. Overlay media uses immutable same-instance document IDs without bytes."
                        ),
                        new SiteGuideFact(
                            "Format and limits",
                            "BlokeBot exports UTF-8 JSON with the identifier blokebot.channel-configuration and format version 1. The maximum file size is 2 MB. Each collection can contain a maximum of 1,000 records. The envelope and typed section records reject unknown properties and enum values. A known core Automation configuration object can remain invalid for repair. BlokeBot accepts format 1 and adapts format 0. BlokeBot rejects all other format versions."
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
                        PhoneAlt: "The import file controls for Sample Channel on a narrow screen.",
                        LaptopAlt: "The import file controls for Sample Channel on a wide screen.",
                        "Upload or paste one BlokeBot JSON document. Check the file before you apply it."
                    ),
                    MediaAfterContent = _configurationTransferReviewMedia,
                    Steps =
                    [
                        "Open Configuration transfer for the destination channel.",
                        "Upload the JSON file, or paste its contents.",
                        "Check the source channel and the optional source BlokeBot version.",
                        "Use the numeric format version to check compatibility.",
                        "Treat the source BlokeBot version as information about the export.",
                        "Select the sections that you want to import.",
                        "Check the counts for additions, updates, skips, and removals.",
                        "Check each section strategy and each feature switch change.",
                        "Resolve each required decision before you select Apply selected sections.",
                    ],
                    Note = "File validation and the preview do not change the destination channel.",
                },
                new SiteGuideSection
                {
                    Heading = "Resolve conflicts",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-conflict.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-conflict.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-conflict.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-conflict.png",
                        PhoneAlt: "Import conflicts for Sample Channel on a narrow screen.",
                        LaptopAlt: "Import conflicts for Sample Channel on a wide screen.",
                        "Resolve each section strategy, alias, dependency, and Guessing profile decision before you apply the import."
                    ),
                    Paragraphs =
                    [
                        "Each conflict describes one choice that blocks the import. Resolve only the conflicts in the selected sections.",
                    ],
                    Facts =
                    [
                        new SiteGuideFact(
                            "Add missing",
                            "Custom commands and Announcements add names that are not in the destination. Guessing skips a profile match. If the destination has no Points settings, Points & giveaways applies the imported record. Otherwise, it skips the record."
                        ),
                        new SiteGuideFact(
                            "Merge",
                            "BlokeBot updates names that match without regard to letter case. It keeps items that are only in the destination. Guessing updates a profile match. Points & giveaways replaces its settings record."
                        ),
                        new SiteGuideFact(
                            "Replace",
                            "Replace uses the Merge rules. It removes replaceable items that are only in the destination. When retained commands or Announcements need shared replies, the replies stay in the destination. If an unmatched Guessing profile has no rounds, Replace can remove it."
                        ),
                        new SiteGuideFact(
                            "Guessing profile targets",
                            "An explicit profile selection overrides the automatic target. Otherwise, Guessing uses an exact canonical slug. If no target profile matches, BlokeBot creates a profile. BlokeBot updates a matched profile in place. The profile keeps its ID and linked rounds."
                        ),
                        new SiteGuideFact(
                            "Chat Tools enablement",
                            "Chat Tools enablement does not use a conflict strategy. The preview shows a separate choice for each On or Off change."
                        ),
                        new SiteGuideFact(
                            "Aliases and dependencies",
                            "For an alias conflict, rename or omit the alias. For an absent command, Overlay, cue, reward, or other host-local dependency, skip the complete imported item or stop the import."
                        ),
                    ],
                    Note =
                        "For Replace, retain each unmatched destination Guessing profile that has rounds. If you cannot retain the profile, stop the import.",
                },
                new SiteGuideSection
                {
                    Heading = "Apply the import",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-success.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-success.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-success.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-success.png",
                        PhoneAlt: "A successful import for Sample Channel on a narrow screen.",
                        LaptopAlt: "A successful import for Sample Channel on a wide screen.",
                        "A successful import lists the changed sections. It also supplies an operation ID and links to the destination settings."
                    ),
                    Facts =
                    [
                        new SiteGuideFact(
                            "Database save",
                            "Apply checks the file again. One transaction saves the selected settings, feature switches, transition boundaries, import audit, and pending activation record. If the transaction fails, BlokeBot saves none of these changes."
                        ),
                        new SiteGuideFact(
                            "Feature activation",
                            "Imported settings do not enable their features. Only selected switch changes can change feature status. After the transaction, BlokeBot starts activation as a separate task. If the task fails or stays incomplete, BlokeBot keeps the imported settings and feature switches."
                        ),
                        new SiteGuideFact(
                            "Replay prevention",
                            "An import does not run general catch-up. BlokeBot does not replay work from a disabled period. Feature changes can move transition boundaries for pause, generation, accept-after, and announcement occurrences."
                        ),
                        new SiteGuideFact(
                            "Post-commit reconciliation",
                            "Overlay refresh and Automation EventSub reconciliation start only after the database commit. If reconciliation fails, the imported configuration remains saved and the failure is reported separately."
                        ),
                    ],
                    Note =
                        "If activation fails, select Retry activation. Do not import the file again.",
                },
                new SiteGuideSection
                {
                    Heading = "Troubleshooting",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-failed.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-failed.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-failed.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-failed.png",
                        PhoneAlt: "A saved Sample Channel import with a failed activation task on a narrow screen.",
                        LaptopAlt: "A saved Sample Channel import with a failed activation task on a wide screen.",
                        "If activation fails after BlokeBot saves the import, select Retry activation. Do not import the file again."
                    ),
                    Paragraphs =
                    [
                        "File and validation errors do not change the destination. Some of these errors do not have an operation ID.",
                        "A rejected import or save failure does not change the destination. An activation failure occurs after BlokeBot saves the import.",
                    ],
                    Facts =
                    [
                        new SiteGuideFact(
                            "File error or unsupported format",
                            "Copy the location and message. Correct the source file. If you cannot correct it, export the file again. Validate the file again."
                        ),
                        new SiteGuideFact(
                            "Import rejected or not saved",
                            "Copy the operation ID and message. Check the selected channel and your authority. Retry the save."
                        ),
                        new SiteGuideFact(
                            "Activation failed",
                            "Select Retry activation. Do not import the file again. Give the operation ID to support."
                        ),
                        new SiteGuideFact(
                            "Unwanted settings",
                            "Check and import the recovery file. Use Replace only for removals that the preview shows."
                        ),
                        new SiteGuideFact(
                            "Feature switches",
                            "Select each feature switch that you must reverse. Do not expect the recovery file to reverse feature switches automatically."
                        ),
                        new SiteGuideFact(
                            "Absent dependency",
                            "If format 1 omits an Automation or Overlay Cue dependency, skip the complete command. Stop the import if you cannot skip it."
                        ),
                        new SiteGuideFact(
                            "Guessing history",
                            "If an unmatched Guessing profile has round history, retain it. Stop the import if you cannot retain the profile."
                        ),
                    ],
                    Note =
                        "Do not edit export-local IDs or their references. Do not give the configuration file to support. Do not expect the recovery file to restore excluded runtime data or history.",
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
