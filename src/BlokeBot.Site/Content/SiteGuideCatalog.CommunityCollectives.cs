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
            Summary = "Manage a Collective.",
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
                    Bullets =
                    [
                        "Invite explicit hosts from one BlokeBot installation.",
                        "Preserve each host's authority.",
                        "Share only bounded projections.",
                        "Membership grants no authority over another host's Twitch connection.",
                        "Membership grants no authority over another host's provider access.",
                        "Membership grants no authority over another host's source mapping.",
                        "Membership grants no authority over another host's lobby details.",
                        "Membership grants no authority over another host's rewards.",
                        "Membership grants no authority over another host's moderator notes.",
                        "Supported projection: a tournament.",
                        "Supported projection: a raid relay.",
                        "Supported projection: a goal.",
                    ],
                    Heading = "Enable explicit consent for each host",
                    Steps =
                    [
                        "Ask each channel owner or permitted moderator to turn on Collectives in Channel setup.",
                        "Create a Collective from one host.",
                        "Use that host as the first Coordinator.",
                        "Invite only known hosts from the same BlokeBot installation.",
                        "Let only the invited host accept or decline for itself.",
                        "Do not treat Twitch raids as membership or consent.",
                        "Do not treat Twitch raids as trust.",
                        "Do not treat follows as membership or consent.",
                        "Do not treat follows as trust.",
                        "Do not treat shared moderators as membership or consent.",
                        "Do not treat shared moderators as trust.",
                        "Do not treat channel relationships as membership or consent.",
                        "Do not treat channel relationships as trust.",
                    ],
                    Paragraphs =
                    [
                        "A collaborator is an active member host in this Collective. It can read the bounded workflow and act only for itself.",
                        "A moderator permission remains host-scoped.",
                        "Each channel starts with this switch off.",
                        "The workspace has no second switch.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage Coordinator and member roles",
                    Bullets =
                    [
                        "A Coordinator can invite known hosts.",
                        "A Coordinator can withdraw a pending invitation.",
                        "A Coordinator can edit shared workflow definitions.",
                        "A Coordinator can transfer coordination.",
                        "A Coordinator can remove bounded participation.",
                        "An active member can leave only for its own host. A pending member can accept or decline only for itself.",
                        "At least one active Coordinator must remain.",
                        "Transfer coordination before the last Coordinator leaves or another user removes that Coordinator.",
                        "BlokeBot rejects a departure that leaves no active Coordinator. It makes no membership change.",
                        "The audit records each membership or authority change.",
                        "The audit records the actor host.",
                        "The audit records the operation reference.",
                        "A repeated accepted operation makes no additional change.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Reference a tournament from one host",
                    Bullets =
                    [
                        "Select an active member as Owning host.",
                        "Enter that host's public competition ID.",
                        "Before the choice, check that the host enabled Tournaments & leagues and owns the competition.",
                        "The Collective does not copy the competition.",
                        "The Collective shares the read-only name and format.",
                        "The Collective shares the read-only status and round.",
                        "The Collective shares the read-only entrant count and confirmed-result count.",
                        "The Collective shares the read-only revision.",
                        "The Owning host remains authoritative.",
                        "The Owning host keeps private entrant contact and lobby details.",
                        "The Owning host keeps moderator notes and rewards.",
                        "The Owning host keeps the result audit.",
                        "Open tournament returns to that workflow.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm each raid relay handoff",
                    Bullets =
                    [
                        "Choose only active members with consent as current and next hosts. Each host must enable Collectives and Raid & collaboration.",
                        "Only the current host confirms its outgoing Twitch raid.",
                        "Shared state contains the current host.",
                        "Shared state contains the next host.",
                        "Shared state contains status.",
                        "Shared state contains audited handoffs.",
                        "Shared state contains the total viewer count.",
                        "Shared state excludes viewer identities.",
                        "After provider work, BlokeBot checks membership.",
                        "After provider work, BlokeBot checks selected-host authority.",
                        "After provider work, BlokeBot checks relay identity.",
                        "After provider work, BlokeBot checks revision.",
                        "After provider work, BlokeBot checks both feature gates.",
                        "After provider work, BlokeBot checks pause watermarks.",
                        "A stale reconfiguration returns a typed non-success.",
                        "A stale revoke returns a typed non-success.",
                        "A stale leave returns a typed non-success.",
                        "A stale disable returns a typed non-success.",
                        "A stale disable-and-re-enable sequence returns a typed non-success.",
                        "It cannot overwrite newer state.",
                        "BlokeBot records one provider rejection with a new revision and audit entry.",
                        "Before a deliberate retry, refresh the relay.",
                        "BlokeBot never reports the rejection as success or replays it later.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "A host must have explicit participation to appear in public output.",
                        "A host must have enabled features to appear in public output.",
                        "A host must have the current allowlisted projection to appear in public output.",
                        "Contributor identities remain local.",
                        "Rewards remain local.",
                        "Balances remain local.",
                        "Notes remain local.",
                        "Source mappings remain local.",
                        "The shared view publishes the target.",
                        "The shared view publishes the current total.",
                        "The shared view publishes the per-host totals.",
                        "The shared view publishes the deadline.",
                        "The shared view publishes the status.",
                    ],
                    Heading = "Combine bounded public goals",
                    Steps =
                    [
                        "As the Coordinator, create the goal name.",
                        "Set the unit.",
                        "Set a positive target.",
                        "Set a future UTC deadline.",
                        "Ask each active host to choose only its own public viewer-funded bounty.",
                        "As that host, enable Bounties and Points before the choice.",
                        "Do not let another host set or expose the private source mapping.",
                    ],
                    Paragraphs =
                    [
                        "The public route and !collective summary include only active hosts that meet all the public-output conditions.",
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
                        "Reload the Collective.",
                        "Compare the selected host and workflow.",
                        "Then reapply the intended local choice.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Disable the feature and restore public output",
                    Bullets =
                    [
                        "Turn off Collectives for a host to remove its navigation and public output.",
                        "With Collectives off for a host, BlokeBot blocks membership work and workflow work before it starts.",
                        "With Collectives off for a host, BlokeBot blocks runtime work and shared-event work before it starts.",
                        "With Collectives off for a host, BlokeBot blocks command work and automation work before it starts.",
                        "With Collectives off for a host, BlokeBot blocks retry work and reconciliation work before it starts.",
                        "With Collectives off for a host, BlokeBot blocks provider work before it starts.",
                        "BlokeBot retains Collectives and consent.",
                        "BlokeBot retains local settings and bounded history.",
                        "BlokeBot retains audits.",
                        "The signed-in direct route explains recovery and links to Channel setup.",
                        "Re-enable the feature to resume retained state from a new watermark.",
                        "BlokeBot does not replay suppressed invitations and events.",
                        "BlokeBot does not replay suppressed timers and retries.",
                        "BlokeBot does not replay suppressed relays and reconciliation.",
                        "BlokeBot does not replay suppressed provider actions.",
                        "If public output disappears, check membership.",
                        "If public output disappears, check the host switch.",
                        "If public output disappears, check its required feature.",
                        "Restore consent or feature availability.",
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
