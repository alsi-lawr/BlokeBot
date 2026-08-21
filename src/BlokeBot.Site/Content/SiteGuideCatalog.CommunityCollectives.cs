namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityCollectivePages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/collectives",
            Eyebrow = "Community progression · Multi-host",
            Title = "Coordinate a multi-host Collective",
            Summary =
                "Invite explicit hosts from one BlokeBot installation. Preserve each host's authority. Share only bounded tournament, raid-relay, or goal projections.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-collectives-recovery.png",
                LightPhoneSource: "media/community/figures/phone-light-collectives-recovery.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-collectives-recovery.png",
                LightLaptopSource: "media/community/figures/laptop-light-collectives-recovery.png",
                PhoneAlt: "The Sample Channel Collectives direct route on a narrow screen that shows retained consent and recovery while the feature is off.",
                LaptopAlt: "The Sample Channel Collectives direct route on a narrow screen that shows retained consent and recovery while the feature is off.",
                "The disabled route preserves consent and workflows. It explains recovery without replay."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable explicit consent for each host",
                    Steps =
                    [
                        "Ask each channel owner or permitted moderator to turn on Collectives in Channel setup. Expect each channel to start with this switch off.",
                        "Do not look for a second switch in the workspace because it has none.",
                        "Create a Collective from one host. Use that host as the first Coordinator. Invite only known hosts from the same BlokeBot installation.",
                        "Let only the invited host accept or decline for itself.",
                        "Do not treat Twitch raids, follows, shared moderators, or channel relationships as membership, consent, or trust.",
                    ],
                    Paragraphs =
                    [
                        "A collaborator is an active member host in this Collective. It can read the bounded workflow and act only for itself.",
                        "A moderator permission remains host-scoped. Membership grants no authority over another host's Twitch connection, provider access, source mapping, lobby details, rewards, or moderator notes.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage Coordinator and member roles",
                    Bullets =
                    [
                        "A Coordinator can invite known hosts, withdraw a pending invitation, edit shared workflow definitions, transfer coordination, and remove bounded participation.",
                        "An active member can leave only for its own host. A pending member can accept or decline only for itself.",
                        "At least one active Coordinator must remain.",
                        "Transfer coordination before the last Coordinator leaves or another user removes that Coordinator.",
                        "BlokeBot rejects the action without a membership change.",
                        "The audit records each membership or authority change, the actor host, and the operation reference. A repeated accepted operation makes no additional change.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Reference a tournament from one host",
                    Bullets =
                    [
                        "Choose an active member as Owning host. Enter that host's public competition ID.",
                        "Before the choice, verify that the host enabled Tournaments & leagues and owns the competition.",
                        "The Collective does not copy the competition. It shares the read-only name, format, status, round, entrant count, confirmed-result count, and revision.",
                        "The Owning host remains authoritative.",
                        "Private entrant contact, lobby details, moderator notes, rewards, and result audit stay with the Owning host. Open tournament returns to that workflow.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm each raid relay handoff",
                    Bullets =
                    [
                        "Choose only active members with consent as current and next hosts. Each host must enable Collectives and Raid & collaboration.",
                        "Only the current host confirms its outgoing Twitch raid. Shared state contains current and next host, status, audited handoffs, and total viewer count.",
                        "Shared state excludes viewer identities.",
                        "After provider work, BlokeBot checks membership, selected-host authority, relay identity, revision, both feature gates, and pause watermarks.",
                        "A stale reconfiguration, revoke, leave, disable, or disable-and-re-enable sequence returns a typed non-success. It cannot overwrite newer state.",
                        "BlokeBot records one provider rejection with a new revision and audit entry.",
                        "Before a deliberate retry, refresh the relay.",
                        "BlokeBot never reports the rejection as success or replays it later.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Combine bounded public goals",
                    Steps =
                    [
                        "As the Coordinator, create the goal name, unit, positive target, and future UTC deadline.",
                        "Ask each active host to choose only its own public viewer-funded bounty.",
                        "As that host, enable Bounties and Points before the choice.",
                        "Do not let another host set or expose the private source mapping.",
                    ],
                    Paragraphs =
                    [
                        "The shared view publishes the target, current total, per-host totals, deadline, and status. Contributor identities, rewards, balances, notes, and source mappings remain local.",
                        "The public route and !collective summary include only active hosts with explicit participation, enabled features, and the current allowlisted projection.",
                    ],
                    Code = "!collective",
                },
                new SiteGuideSection
                {
                    Heading = "Save only settings for one host",
                    Bullets =
                    [
                        "The workflow editor changes Collective definitions only if the selected host can coordinate.",
                        "The Details sidecar identifies private settings for the selected host. These settings include its goal source and notification audience.",
                        "Save local settings is the only sticky Save in this workspace. It appears only after a genuine local change.",
                        "A stale revision returns a conflict.",
                        "Reload the Collective. Compare the selected host and workflow. Then reapply the intended local choice.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Disable the feature and restore public output",
                    Bullets =
                    [
                        "Turn off Collectives for a host to remove its navigation and public output.",
                        "BlokeBot then blocks membership, workflow, runtime, shared-event, command, automation, retry, reconciliation, and provider work before it starts.",
                        "BlokeBot retains Collectives, consent, local settings, bounded history, and audits. The signed-in direct route explains recovery and links to Channel setup.",
                        "Re-enable the feature to resume retained state from a new watermark.",
                        "BlokeBot does not replay suppressed invitations, events, timers, retries, relays, reconciliation, or provider actions.",
                        "If public output disappears, verify membership, the host switch, and its required feature. Restore consent or feature availability.",
                        "BlokeBot never uses private state as a fallback projection.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Run tournaments and leagues", "community/competitions"),
                new SiteLink("Use the raid and collaboration hub", "community/raid-collaboration"),
                new SiteLink("Run viewer-funded bounties", "community/bounties"),
            ],
        };
    }
}
