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
            Summary =
                "Open a clear challenge and let viewers reserve points toward it. Settle each outcome and show who contributed.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-bounties-public-board.png",
                LightPhoneSource: "media/community/progression/phone-light-bounties-public-board.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-bounties-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-bounties-setup.png",
                PhoneAlt: "The Sample Channel public bounty board that shows a funding challenge, total, deadline and recorded contributor Twitch logins.",
                LaptopAlt: "The Sample Channel Bounties management page that shows the proposed-bounty setup fields, visibility and point settlement choices.",
                "Owners and moderators configure Bounties in the dashboard. Participants fund Public challenges on the board or in chat."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn on Bounties and Points",
                    Steps =
                    [
                        "Choose the channel, open Channel setup and turn on Points and Bounties under Chat tools. Turn on both switches before Bounties appears in normal navigation or accepts work.",
                        "The feature cards persist those switch changes immediately. Open Bounties and use the Page help (?) button if you need the in-dashboard summary.",
                        "A channel owner or permitted moderator creates and settles bounties. A participant needs a Twitch chat identity and enough available BlokeBot points to pledge.",
                    ],
                    Note =
                        "Bounties use BlokeBot points. This feature cannot debit or pay out Twitch Channel Points.",
                },
                new SiteGuideSection
                {
                    Heading = "Create a proposal and open funding",
                    Steps =
                    [
                        "Enter the public title and description, funding target, UTC expiry and optional fixed completion-bonus pool.",
                        "Choose Public or Private visibility, what a Failed outcome does with pledges, and Equal or Proportional completion-bonus distribution.",
                        "Put staff-only context in Private moderator note, then select Create proposed bounty. Proposed is a draft and cannot receive pledges.",
                        "Review the selected channel and values, add a Private audit reason, then select Open funding.",
                    ],
                    Paragraphs =
                    [
                        "The lifecycle is Proposed, Funding, Accepted and one terminal outcome: Completed, Failed, Expired or Cancelled. Reject is a distinct audited action. It stores a Cancelled outcome.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pledge and follow progress",
                    Bullets =
                    [
                        "The public board is /bounties/{channel}. Replace the value in braces with the channel login. Public bounties show the title, description, state, funding total, target, deadline, bonus, terminal history and recorded contributors.",
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
                        LaptopAlt: "The Sample Channel funding bounty that shows contributor logins, pledge totals, moderator actions, a private audit reason and expiry extension.",
                        "Funding and moderation remain reviewable in the dashboard. A retained signed-in direct route points back to Channel setup while Bounties is off."
                    ),
                    Bullets =
                    [
                        "You can extend Funding and Accepted bounties before they become terminal. Either state can expire. Check the UTC expiry before you extend it.",
                        "An Accepted bounty can move to Completed, Failed or Cancelled. Every action records the authenticated actor, time, action and private audit reason.",
                        "Completed consumes all reserved pledges. BlokeBot splits its fixed bonus pool across contributor logins with the selected Equal or Proportional rule. It cannot grant twice.",
                        "Reject, Cancel and Expire refund every reserved pledge. Fail applies the bounty's chosen Refund pledges or Spend pledges policy exactly once.",
                    ],
                    Paragraphs =
                    [
                        "If another moderator changed the bounty, reload before you act. BlokeBot rejects a stale transition and keeps the newer state.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Understand identity and privacy",
                    Bullets =
                    [
                        "A Public bounty exposes each recorded normalized Twitch login and its total pledge amount. A Private bounty publishes no bounty data.",
                        "BlokeBot groups contributors by that host-scoped recorded login. Point debits, refunds and bonuses also use it. A later Twitch rename does not move the balance or combine historical logins.",
                        "The public board and chat summary never contain private moderator notes, audit reasons, raw provider data or internal identifiers.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover without double settlement",
                    Bullets =
                    [
                        "If Points is off, turn it on in Channel setup. Saved bounty work remains unchanged while the dependency is unavailable.",
                        "If BlokeBot rejects a pledge, correct the visible balance, state, expiry or validation cause. Submit once. If a pledge or transition is visible, reload. Do not repeat it.",
                        "If Bounties is off, BlokeBot hides navigation, commands and public data. It stops pledges, moderation, expiry work, ledger changes and emitted events. Saved bounties, pledges and history remain.",
                        "Re-enable Bounties and Points to continue from retained current state. Commands, expiries, events and other work suppressed while off are not replayed.",
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
            Title = "Run seasons, quests and achievements",
            Summary =
                "Turn supported channel events into individual or communal progress, then preserve standings and viewer-earned rewards beyond the season.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-community-progression-public.png",
                LightPhoneSource: "media/community/progression/phone-light-community-progression-public.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-community-progression-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-community-progression-setup.png",
                PhoneAlt: "The Sample Channel public season page that shows named standings and current viewer quest progress on a narrow screen.",
                LaptopAlt: "The Sample Channel Community progression page that shows new-season dates, Public visibility and private moderator notes.",
                "The management page starts the season contract. The public page makes named standings and progress visible when the season is Public."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose authority and visibility",
                    Steps =
                    [
                        "Choose the channel, open Channel setup and turn on Community progression under Chat tools. The feature card persists the change immediately.",
                        "A channel owner or permitted moderator creates seasons, definitions and rewards and controls lifecycle and reset schedules. Participant progress comes from authenticated Twitch chat and supported Twitch or BlokeBot events.",
                        "Choose Public to publish participant Twitch identities and progression, or Hidden to publish no progression data. Private moderator notes stay on the management page in both modes.",
                    ],
                    Paragraphs =
                    [
                        "Public seasons show Twitch display names and recorded logins in standings. They show individual quest and achievement progress, communal goals and completions. They also show equipped rewards, unlock history and archived history. They never expose raw provider payloads, provider credentials, internal IDs, moderator notes or internal audit material.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Create the season contract",
                    Steps =
                    [
                        "Create a Draft season with a name, public description, UTC start and end and Public or Hidden visibility.",
                        "While it is Draft, add host-scoped Title, Badge icon or Cosmetic accent rewards. Select only supported presentation tokens. Arbitrary CSS is not accepted.",
                        "Add Quest or Achievement definitions. Choose per-viewer or channel-wide communal progress. Choose One-time or Repeatable completion, a target, optional reward keys and one supported event rule.",
                        "Open the season only after the definition and reward inventory is complete. Progress events outside the open season's start and end are not counted.",
                    ],
                    Bullets =
                    [
                        "Typed rules cover chat messages, follows, subscriptions, cheers, incoming raids, reward redemptions, completed bounties and predeclared external achievement grants.",
                        "Definitions allow only supported rule, owner, increment and filter combinations. A rejected combination remains unsaved. Choose a compatible option. Do not treat the event as generic text.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Follow individual and communal progress",
                    Bullets =
                    [
                        "A host-scoped event that matches advances each applicable active definition once. Event-value rules use the supported numeric value, while occurrence rules add one.",
                        "Per-viewer definitions update that participant's current progress, completions and standings. Communal definitions combine events that qualify into one channel-wide goal.",
                        "Participants use !progress for a short view of current Public season progress. They use /community/{channel} for standings, goals, completions and rewards.",
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
                        "Daily and weekly repeatable definitions use the channel timezone, the configured local time and, for weekly resets, the configured weekday. The page shows the next UTC reset.",
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
                        "A completion grants its configured points, title, badge icon and supported cosmetic accent atomically. A retry of the same completion does not grant twice.",
                        "Viewer unlocks survive season closure and archival. A viewer can equip one unlocked title, badge and accent for this host with the chat commands shown above.",
                        "An equip action checks reward ownership and host scope. It changes the current selection and does not rewrite the immutable season completion record.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Close archive and recover",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-community-progression-disabled.png",
                        LightPhoneSource: "media/community/progression/phone-light-community-progression-disabled.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-community-progression-archive.png",
                        LightLaptopSource: "media/community/progression/laptop-light-community-progression-archive.png",
                        PhoneAlt: "The Sample Channel Community progression direct route that shows retained-state recovery while the feature is off.",
                        LaptopAlt: "The Sample Channel public progression page that shows completed achievement history, persistent reward unlocks and an archived season standings snapshot.",
                        "A close action preserves a final standings snapshot and completion history. A disable action preserves the same data and routes staff back to Channel setup."
                    ),
                    Bullets =
                    [
                        "Close and snapshot standings freezes final standings and completion history. Archive keeps that snapshot and every persistent viewer unlock and equipped selection.",
                        "If expected progress is absent, confirm the channel, dates, rule, scope, filter, period and visibility. Then send one new event.",
                        "If Community progression is off, BlokeBot stops commands, events, timers, automation, rewards and public output before mutation. Seasons, progress, schedules, rewards and history remain saved.",
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
