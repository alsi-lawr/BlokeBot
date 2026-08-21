namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static readonly SiteMedia _configurationTransferReviewMedia = new(
        DarkPhoneSource: "media/configuration-transfer/phone-dark-review.png",
        LightPhoneSource: "media/configuration-transfer/phone-light-review.png",
        DarkLaptopSource: "media/configuration-transfer/laptop-dark-review.png",
        LightLaptopSource: "media/configuration-transfer/laptop-light-review.png",
        PhoneAlt: "Validated Sample Channel import with section strategies and change counts on a narrow screen.",
        LaptopAlt: "Validated Sample Channel import with section strategies and change counts on a wide screen.",
        "The preview reports what will be added, updated, skipped or removed before anything is saved."
    );

    private static IEnumerable<SiteGuidePage> CreateConfigurationTransferPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/configuration-transfer",
            Eyebrow = "Configuration portability",
            Title = "Export and move supported channel configuration",
            Summary =
                "Export selected supported settings, preview changes before applying them to another channel, and retry files that fail before commit. Configuration transfer is not a full channel backup or an automatic rollback.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/configuration-transfer/phone-dark-export.png",
                LightPhoneSource: "media/configuration-transfer/phone-light-export.png",
                DarkLaptopSource: "media/configuration-transfer/laptop-dark-export.png",
                LightLaptopSource: "media/configuration-transfer/laptop-light-export.png",
                PhoneAlt: "Sample Channel configuration sections selected for export on a narrow screen.",
                LaptopAlt: "Sample Channel configuration sections selected for export on a wide screen.",
                "Select the source channel, then select the configuration sections to export."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare",
                    Paragraphs =
                    [
                        "To export, select the source channel whose settings you want to copy. To import, select the destination channel that may be changed.",
                    ],
                    Bullets =
                    [
                        "Configuration transfer is available to signed-in operators for the selected channel: the broadcaster, an administrator or a moderator.",
                        "Applying as a moderator rechecks saved channel access and current Twitch moderator authority.",
                        "Before importing, export the destination's current supported configuration as a private recovery snapshot. The snapshot covers only the supported sections, so it is not a full backup or guaranteed rollback.",
                        "Custom commands exports include command access rules and allowlisted users' Twitch IDs, logins and display names. There is no separate access-settings switch, and all five sections are selected by default. Keep every export private.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Export",
                    Steps =
                    [
                        "Open Configuration transfer for the source channel and review the five selected sections.",
                        "Clear any section that should not be included, then export and privately store the JSON file.",
                    ],
                    Bullets =
                    [
                        "Custom commands: dashboard time-zone ID; reusable replies; custom counter names and values; command names, enabled state, aliases, access rules, allowlisted Twitch users, cooldowns, invocation limits, action type, and reply or counter routes.",
                        "Announcements: used replies; enabled state; chat or Twitch delivery, color, retry and lifetime policy, and interval, chat-threshold or fixed-UTC weekly schedules.",
                        "Weekly schedules store a UTC weekday and UTC time. An Announcements-only import keeps the destination time-zone ID, so the same recurrence can display on a different local day or time and can shift with daylight saving. Custom commands owns the dashboard time-zone ID: Merge or Replace imports it; Add missing leaves the destination time zone unchanged. Reprojecting the editor display does not change the stored UTC recurrence.",
                        "Guessing: profiles, canonical slugs, default state, winning-point rewards, command aliases, reply text, answers and reply targets.",
                        "Points & giveaways: point label, command aliases, reply text, gambling rules and giveaway rules. Point balances and ledgers are not included.",
                        "Chat Tools enablement: twenty independent feature switches. Only differences selected during review are applied.",
                        "Exports do not contain credentials, sessions, cookies, server or deployment settings, point balances or ledgers, Guessing rounds or votes, leaderboards, giveaway entrants or draws, durable alerts, queued public chat, delivery receipts, stream or community runtime state, Automation or Overlay Cue definitions, or raw database keys. Export-local references replace database keys, but Custom commands may contain allowlisted users' Twitch IDs.",
                        "BlokeBot exports UTF-8 JSON identified as blokebot.channel-configuration, format version 1. Files are limited to 2 MB and each collection to 1,000 records. Version 1 rejects unknown properties and enum values. BlokeBot accepts version 1 and adapts version 0; all other versions, including future versions, are rejected.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Import",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-upload.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-upload.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-upload.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-upload.png",
                        PhoneAlt: "Configuration file upload and paste choices for Sample Channel on a narrow screen.",
                        LaptopAlt: "Configuration file upload and paste choices for Sample Channel on a wide screen.",
                        "Upload or paste a bounded BlokeBot JSON document. No setting changes before validation and confirmation."
                    ),
                    MediaAfterContent = _configurationTransferReviewMedia,
                    Steps =
                    [
                        "Open Configuration transfer for the destination channel, then upload the JSON file or paste its contents.",
                        "Check the source channel, optional source BlokeBot version and numeric format version. The format version determines compatibility; the source BlokeBot version is review metadata.",
                        "Select the sections to import. Review the added, updated, skipped and removed counts, each applicable section strategy, and each displayed enablement difference.",
                        "Resolve every required decision before Apply. Validation and preview do not change the destination.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Conflicts",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-conflict.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-conflict.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-conflict.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-conflict.png",
                        PhoneAlt: "Sample Channel import conflicts that require decisions on a narrow screen.",
                        LaptopAlt: "Sample Channel import conflicts that require decisions on a wide screen.",
                        "Resolve section strategies, aliases, dependencies and Guessing profile decisions before applying the import."
                    ),
                    Bullets =
                    [
                        "Custom commands and Announcements: Add missing adds names not already present; Merge updates case-insensitive name matches and keeps destination-only items; Replace does the same and removes destination-only replaceable items. Shared replies needed by retained commands or Announcements stay in place.",
                        "Guessing: an explicit profile mapping wins; otherwise profiles match an exact canonical slug. Add missing skips matches, Merge updates matches, and Replace also removes unmatched profiles only when they have no rounds.",
                        "Points & giveaways: Add missing applies only when the destination has no Points settings. Merge and Replace both replace the single settings record.",
                        "Chat Tools enablement: no conflict strategy applies. Select each displayed On or Off difference independently.",
                        "For each imported Guessing profile, keep the automatic target or choose a destination profile. An explicit choice wins; otherwise BlokeBot matches the exact canonical slug or creates a profile. A matched target is updated in place, so its ID and linked rounds remain.",
                        "With Replace section, separately choose Retain for every unmatched destination Guessing profile that has rounds, or abort. Only unmatched profiles without rounds can be removed.",
                        "For a conflicting alias, rename or omit that alias, or abort. For an Automation or Overlay Cue dependency, skip the whole command or abort.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Apply",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-success.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-success.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-success.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-success.png",
                        PhoneAlt: "Successful Sample Channel configuration import summary on a narrow screen.",
                        LaptopAlt: "Successful Sample Channel configuration import summary on a wide screen.",
                        "A successful import names the changed sections, provides an operation ID and links to the destination configuration."
                    ),
                    Bullets =
                    [
                        "Apply revalidates and saves all selected configuration changes, selected feature switches, transition boundaries, the import audit and any pending activation record in one database transaction. If that commit fails, none of those changes are saved.",
                        "Importing configuration alone does not enable its feature. Only separately checked switch differences change enablement.",
                        "After commit, BlokeBot runs feature lifecycle activation separately. It may be pending or fail. A failed activation leaves the imported configuration and feature switches saved; use Retry activation instead of importing again.",
                        "Import-based enabling does not run generic catch-up, so work missed while disabled is not replayed.",
                        "Balances, ledgers, Guessing rounds and votes, giveaway runtime and sent-delivery history are not imported. Selected feature transitions can advance pause, generation, accept-after and announcement occurrence boundaries to enforce the no-replay rule.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover",
                    MediaAfterHeading = new SiteMedia(
                        DarkPhoneSource: "media/configuration-transfer/phone-dark-failed.png",
                        LightPhoneSource: "media/configuration-transfer/phone-light-failed.png",
                        DarkLaptopSource: "media/configuration-transfer/laptop-dark-failed.png",
                        LightLaptopSource: "media/configuration-transfer/laptop-light-failed.png",
                        PhoneAlt: "Sample Channel import saved with feature activation failed and Retry activation on a narrow screen.",
                        LaptopAlt: "Sample Channel import saved with feature activation failed and Retry activation on a wide screen.",
                        "An activation failure happens after the import commits. Retry only the separate activation step."
                    ),
                    Bullets =
                    [
                        "File or validation error: copy the displayed location and message. These errors may not have an operation ID. Correct or re-export the source file, then validate again; the destination has not changed.",
                        "A future or other unsupported format version is rejected. Correct or re-export a supported source file, then validate again.",
                        "Import rejected or not saved: copy the operation ID and message. Review the selected channel and your authority, or retry the save. The destination has not changed.",
                        "Feature activation failed: the import is already saved. Use Retry activation; do not re-import the file.",
                        "Share an operation ID with support, not the configuration file. Do not edit export-local IDs or their references.",
                        "If an unintended import committed, review and re-import the destination snapshot. Use Replace section only where its previewed removals are intended, and separately select any enablement changes that should be reversed. The snapshot cannot restore excluded runtime or history data. Automation or Overlay Cue commands whose dependencies are outside format 1 must be skipped whole or the import must be aborted. Unmatched Guessing profiles with round history must be retained or the import must be aborted.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Manage channel tools", "tools"),
                new SiteLink("Review moderator access", "moderators"),
                new SiteLink("Troubleshoot BlokeBot", "troubleshooting"),
            ],
        };
    }
}
