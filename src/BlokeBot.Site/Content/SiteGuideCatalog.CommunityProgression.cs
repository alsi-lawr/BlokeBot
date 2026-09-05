namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityProgressionPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/bounties",
            Eyebrow = "Community progression · Bounties",
            Title = "Fund channel challenges with viewer points",
            Summary = "Manage community challenges.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-bounties-public-board.png",
                LightPhoneSource: "media/community/progression/phone-light-bounties-public-board.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-bounties-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-bounties-setup.png",
                PhoneAlt: "The Sample Channel public bounty board shows a funding challenge and its total. The deadline and recorded contributor Twitch logins are visible.",
                LaptopAlt: "The Sample Channel Bounties management page shows the fields for a proposed bounty. Visibility and point settlement choices are also visible.",
                "Owners and moderators configure Bounties in the dashboard. Participants fund Public challenges on the board or in chat."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Open a clear challenge.",
                        "Let viewers reserve points toward it.",
                        "Settle each outcome.",
                        "Show who contributed.",
                    ],
                    Heading = "Turn on Bounties and Points",
                    Paragraphs =
                    [
                        "Both switches must be on for Bounties navigation and work.",
                        "The feature cards save those switch changes immediately.",
                        "A channel owner or permitted moderator creates and settles bounties. A participant needs a Twitch chat identity and enough available BlokeBot points to pledge.",
                    ],
                    Steps =
                    [
                        "Select the channel.",
                        "Open Channel setup.",
                        "Turn on Points and Bounties under Chat tools.",
                        "Open Bounties.",
                        "If you need the dashboard summary, use the Page help (?) button.",
                    ],
                    Note =
                        "Bounties use BlokeBot points. This feature cannot debit or pay out Twitch Channel Points.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Non-terminal bounty state: Proposed.",
                        "Non-terminal bounty state: Funding.",
                        "Non-terminal bounty state: Accepted.",
                        "Terminal bounty outcome: Completed.",
                        "Terminal bounty outcome: Failed.",
                        "Terminal bounty outcome: Expired.",
                        "Terminal bounty outcome: Cancelled.",
                    ],
                    Heading = "Create a proposal and open funding",
                    Steps =
                    [
                        "Enter the public title and description.",
                        "Enter the funding target.",
                        "Enter the UTC expiry.",
                        "Enter an optional fixed pool for completion bonuses.",
                        "Select Public or Private visibility.",
                        "Select what a Failed outcome does with pledges.",
                        "Select Equal or Proportional distribution for completion bonuses.",
                        "Put staff-only context in Private moderator note.",
                        "Select Create proposed bounty.",
                        "Review the selected channel and values.",
                        "Add a Private audit reason.",
                        "Select Open funding.",
                    ],
                    Paragraphs =
                    [
                        "The bounty lifecycle ends with one terminal outcome. Reject is a distinct audited action. It stores a Cancelled outcome.",
                        "Proposed is a draft and cannot receive pledges.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pledge and follow progress",
                    Bullets =
                    [
                        "The public board is /bounties/{channel}. Replace the value in braces with the channel login.",
                        "Public bounties show the title and the description.",
                        "Public bounties show the state and the funding total.",
                        "Public bounties show the target and the deadline.",
                        "Public bounties show the bonus and terminal history.",
                        "Public bounties show recorded contributors.",
                        "A signed-in participant enters Pledge points on the board, or uses chat. BlokeBot reserves the accepted amount from that channel's available point balance.",
                        "BlokeBot caps a pledge request above the required amount to the target remainder. It does not overfund the bounty. A repeated delivery records the pledge only once.",
                        "Owners and moderators can select Accept while the state is Funding. They can do this before or after contributions reach the target. When contributions reach the target, BlokeBot does not accept the challenge automatically.",
                    ],
                    Code = "!bounties\n!bounty <bounty-id>\n!bountypledge <bounty-id> <points>",
                    Note =
                        "The words in angle brackets describe a value. Use the public bounty reference that BlokeBot shows. Do not type the brackets.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate deadlines and outcomes",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-bounties-disabled.png",
                        LightPhoneSource: "media/community/progression/phone-light-bounties-disabled.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-bounties-moderation.png",
                        LightLaptopSource: "media/community/progression/laptop-light-bounties-moderation.png",
                        PhoneAlt: "The Sample Channel Bounties direct route that shows retained-state recovery while the feature is off.",
                        LaptopAlt: "The Sample Channel funding bounty shows contributor logins and pledge totals. Moderator actions and a private audit reason are visible. Expiry extension is also visible.",
                        "Funding and moderation remain reviewable in the dashboard. A retained signed-in direct route points back to Channel setup while Bounties is off."
                    ),
                    Bullets =
                    [
                        "You can extend Funding and Accepted bounties before they become terminal. Either state can expire. Check the UTC expiry before you extend it.",
                        "An Accepted bounty can move to Completed.",
                        "An Accepted bounty can move to Failed.",
                        "An Accepted bounty can move to Cancelled.",
                        "Every bounty action records the authenticated actor.",
                        "Every bounty action records the time.",
                        "Every bounty action records the action.",
                        "Every bounty action records the private audit reason.",
                        "Completed consumes all reserved pledges. BlokeBot splits its fixed bonus pool across contributor logins with the selected Equal or Proportional rule. It cannot grant twice.",
                        "Reject refunds every reserved pledge.",
                        "Cancel refunds every reserved pledge.",
                        "Expire refunds every reserved pledge.",
                        "Fail applies the bounty's chosen Refund pledges or Spend pledges policy exactly once.",
                    ],
                    Paragraphs =
                    [
                        "If another moderator changed the bounty, reload before you act. BlokeBot rejects a stale transition and keeps the newer state.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Identity and privacy",
                    LegacyAnchor = "understand-identity-and-privacy",
                    Bullets =
                    [
                        "A Public bounty exposes each recorded normalized Twitch login and its total pledge amount. A Private bounty publishes no bounty data.",
                        "BlokeBot groups contributors by that host-scoped recorded login.",
                        "Point debits use that host-scoped recorded login.",
                        "Point refunds use that host-scoped recorded login.",
                        "Point bonuses use that host-scoped recorded login.",
                        "A later Twitch rename does not move the balance or combine historical logins.",
                        "The public board and chat summary never contain private moderator notes and audit reasons.",
                        "The public board and chat summary never contain raw provider data and internal identifiers.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover without double settlement",
                    Bullets =
                    [
                        "If Points is off, turn it on in Channel setup. Saved bounty work remains unchanged while the dependency is unavailable.",
                        "If BlokeBot rejects a pledge, correct the visible cause of rejection.",
                        "Possible pledge rejection cause: the balance.",
                        "Possible pledge rejection cause: the state.",
                        "Possible pledge rejection cause: the expiry.",
                        "Possible pledge rejection cause: validation.",
                        "Submit once.",
                        "If a pledge or transition is visible, reload.",
                        "Do not repeat it.",
                        "If Bounties is off, BlokeBot hides navigation.",
                        "If Bounties is off, BlokeBot hides commands.",
                        "If Bounties is off, BlokeBot hides public data.",
                        "If Bounties is off, BlokeBot stops pledges.",
                        "If Bounties is off, BlokeBot stops moderation.",
                        "If Bounties is off, BlokeBot stops expiry work.",
                        "If Bounties is off, BlokeBot stops ledger changes.",
                        "If Bounties is off, BlokeBot stops emitted events.",
                        "If Bounties is off, BlokeBot keeps saved bounties.",
                        "If Bounties is off, BlokeBot keeps saved pledges.",
                        "If Bounties is off, BlokeBot keeps saved history.",
                        "Re-enable Bounties and Points to continue from retained current state.",
                        "BlokeBot does not replay suppressed commands from the disabled period.",
                        "BlokeBot does not replay suppressed expiries from the disabled period.",
                        "BlokeBot does not replay suppressed events from the disabled period.",
                        "BlokeBot does not replay suppressed other work from the disabled period.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Build seasons and achievements", "community/progression"),
                new SiteLink("Review viewer points", "points"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/progression",
            Eyebrow = "Community progression · Seasons",
            Title = "Community progression",
            Summary =
                "Turn supported channel events into individual or communal progress. Keep standings and viewer-earned rewards beyond the season.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-community-progression-public.png",
                LightPhoneSource: "media/community/progression/phone-light-community-progression-public.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-community-progression-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-community-progression-setup.png",
                PhoneAlt: "The Sample Channel public season page that shows named standings and current viewer quest progress on a narrow screen.",
                LaptopAlt: "The Sample Channel Community progression page shows new-season dates and Public visibility. Private moderator notes are also visible.",
                "The management page starts the season contract. The public page makes named standings and progress visible when the season is Public."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Participant progress comes from authenticated Twitch chat.",
                        "Supported Twitch events can also supply participant progress.",
                        "Supported BlokeBot events can also supply participant progress.",
                        "A channel owner or permitted moderator creates seasons.",
                        "A channel owner or permitted moderator creates definitions.",
                        "A channel owner or permitted moderator creates rewards.",
                        "Public seasons never expose raw provider payloads.",
                        "Public seasons never expose provider credentials.",
                        "Public seasons never expose internal IDs.",
                        "Public seasons never expose moderator notes.",
                        "Public seasons never expose internal audit material.",
                        "Public seasons also show equipped rewards.",
                        "Public seasons also show unlock history.",
                        "Public seasons also show archived history.",
                        "Public seasons show individual quest progress.",
                        "Public seasons show individual achievement progress.",
                        "Public seasons show communal goals.",
                        "Public seasons show completions.",
                    ],
                    Heading = "Choose authority and visibility",
                    Steps =
                    [
                        "Select the channel.",
                        "Open Channel setup.",
                        "Turn on Community progression under Chat tools.",
                        "Choose Public to publish participant Twitch identities and progression, or Hidden to publish no progression data.",
                    ],
                    Paragraphs =
                    [
                        "Public seasons show Twitch display names and recorded logins in standings.",
                        "The feature card saves the change immediately.",
                        "The channel owner or permitted moderator also controls lifecycle and reset schedules.",
                        "Private moderator notes stay on the management page in both modes.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Create the season contract",
                    Paragraphs =
                    [
                        "BlokeBot does not accept arbitrary CSS.",
                        "BlokeBot does not count progress events outside the open season's start and end.",
                    ],
                    Steps =
                    [
                        "Create a Draft season.",
                        "Set its name and public description.",
                        "Set its UTC start and end.",
                        "Select Public or Hidden visibility.",
                        "While it is Draft, add host-scoped rewards from the supported kinds.",
                        "Select only supported presentation tokens.",
                        "Add Quest or Achievement definitions.",
                        "Select per-viewer or channel-wide communal progress.",
                        "Select One-time or Repeatable completion.",
                        "Select a target.",
                        "Select optional reward keys.",
                        "Select one supported event rule.",
                        "Open the season only after the definition and reward inventory is complete.",
                    ],
                    Bullets =
                    [
                        "Reward kind: Title.",
                        "Reward kind: Badge icon.",
                        "Reward kind: Cosmetic accent.",
                        "Typed rules cover chat messages and follows.",
                        "Typed rules cover subscriptions and cheers.",
                        "Typed rules cover incoming raids and reward redemptions.",
                        "Typed rules cover completed bounties and predeclared external achievement grants.",
                        "Definitions allow only supported combinations of these dimensions:",
                        "Definition dimension: rule.",
                        "Definition dimension: owner.",
                        "Definition dimension: increment.",
                        "Definition dimension: filter.",
                        "A rejected combination remains unsaved. Choose a compatible option. Do not treat the event as generic text.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Follow individual and communal progress",
                    Bullets =
                    [
                        "A host-scoped event that matches advances each applicable active definition once. Event-value rules use the supported numeric value, while occurrence rules add one.",
                        "Per-viewer definitions update that participant's current progress.",
                        "Per-viewer definitions update that participant's completions.",
                        "Per-viewer definitions update that participant's standings.",
                        "Communal definitions combine events that qualify into one channel-wide goal.",
                        "Participants use !progress for a short view of current Public season progress.",
                        "Participants use /community/{channel} for standings.",
                        "Participants use /community/{channel} for goals.",
                        "Participants use /community/{channel} for completions.",
                        "Participants use /community/{channel} for rewards.",
                        "Authorized staff can manage Hidden seasons. The !progress command reports that progression is hidden. The public route publishes no season data.",
                    ],
                    Code =
                        "!progress\n!equiptitle <reward-key>\n!equipbadge <reward-key>\n!equipaccent <reward-key>",
                },
                new SiteGuideSection
                {
                    Heading = "Schedule repeatable resets",
                    Bullets =
                    [
                        "Daily and weekly repeatable definitions use the channel time zone.",
                        "These definitions also use the configured local time.",
                        "Weekly resets also use the configured weekday.",
                        "The page shows the next UTC reset.",
                        "A local time in a daylight-saving gap moves forward to the first valid instant. A local time in an overlap uses its first occurrence and does not reset again at the second.",
                        "After downtime, BlokeBot rolls at most once into the current period. It does not replay every missed daily or weekly period.",
                        "If you save a schedule change during an active period, BlokeBot closes that period. It resets active repeatable progress. Select Reset active progress now before Save schedule and roll over immediately. The confirmed change applies once across retries and multiple instances.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Grant and equip persistent rewards",
                    Bullets =
                    [
                        "A completion grants all its configured completion rewards atomically:",
                        "Completion reward: points.",
                        "Completion reward: a title.",
                        "Completion reward: a badge icon.",
                        "Completion reward: a supported cosmetic accent.",
                        "A retry of the same completion does not grant twice.",
                        "Viewer unlocks survive season closure and archival.",
                        "The chat commands above let a viewer equip one unlocked title for this host.",
                        "The chat commands above let a viewer equip one unlocked badge for this host.",
                        "The chat commands above let a viewer equip one unlocked accent for this host.",
                        "An equip action checks reward ownership and host scope. It changes the current selection and does not rewrite the immutable season completion record.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Season lifecycle",
                    LegacyAnchor = "close-archive-and-recover",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-community-progression-disabled.png",
                        LightPhoneSource: "media/community/progression/phone-light-community-progression-disabled.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-community-progression-archive.png",
                        LightLaptopSource: "media/community/progression/laptop-light-community-progression-archive.png",
                        PhoneAlt: "The Sample Channel Community progression direct route that shows retained-state recovery while the feature is off.",
                        LaptopAlt: "The Sample Channel public progression page shows completed achievement history and persistent reward unlocks. It also shows a snapshot of archived season standings.",
                        "A close action preserves a final standings snapshot and completion history. A disable action preserves the same data and routes staff back to Channel setup."
                    ),
                    Bullets =
                    [
                        "Close and snapshot standings freezes final standings and completion history.",
                        "Archive keeps that snapshot.",
                        "Archive keeps every persistent viewer unlock.",
                        "Archive keeps every equipped selection.",
                        "If expected progress is absent, check the channel and dates.",
                        "Check the rule.",
                        "Check the scope.",
                        "Check the filter.",
                        "Check the period and visibility.",
                        "Send one new event.",
                        "If Community progression is off, BlokeBot stops commands and events before changes.",
                        "If Community progression is off, BlokeBot stops timers and automation before changes.",
                        "If Community progression is off, BlokeBot stops rewards and public output before changes.",
                        "While Community progression is off, BlokeBot keeps saved seasons.",
                        "While Community progression is off, BlokeBot keeps saved progress.",
                        "While Community progression is off, BlokeBot keeps saved schedules.",
                        "While Community progression is off, BlokeBot keeps saved rewards.",
                        "While Community progression is off, BlokeBot keeps saved history.",
                        "Re-enable to continue in the current period. BlokeBot does not replay suppressed events or every reset period missed while the feature was off.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Run Stream-event Bingo", "community/bingo"),
                new SiteLink("Use viewer-funded bounties", "community/bounties"),
            ],
        };
    }
}
