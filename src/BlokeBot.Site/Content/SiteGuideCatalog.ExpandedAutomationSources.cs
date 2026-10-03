namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuideSection> CreateExpandedAutomationSourceSections()
    {
        yield return new()
        {
            Heading = "Ads, countdowns and stream timing",
            Bullets =
            [
                "Ad timing can warn before Twitch's next scheduled ad, observe an ad starting, warn about remaining time, or report its expected end. The end is derived from the observed start and duration; it is not confirmation that viewers finished seeing an ad.",
                "A changed, snoozed or unavailable ad schedule replaces or removes pending derived warnings. The upcoming ad's duration is not evidence of the previous ad's duration. Ad sources request channel:read:ads only when selected in enabled flows.",
                "Countdown lifecycle selects a channel-local named timer's Started, Remaining, Finished, Cancelled or Reset event. Remaining fires only on a future crossing from above the chosen threshold. Starting a three-minute timer with a five-minute warning does not fire an immediate warning.",
                "Scheduled time supports one occurrence, a fixed interval or a weekly local clock time in the selected named time zone. The stored zone and local time resolve to UTC, not the server's local zone. A nonexistent spring-forward time is skipped, with an inspector explanation for the configured gap; a repeated autumn time uses its earlier UTC occurrence only. If an interval starts in a gap, invalid slots on its original local start-plus-interval cadence are skipped. Its first valid slot anchors fixed UTC intervals: London 01:30 with ten-minute slots on 29 March 2026 first runs at 02:00 local (01:00 UTC), then 01:10 UTC.",
                "Fixed intervals keep their scheduled rhythm, not the preceding flow's completion time. Occurrences missed while the bot is stopped or disconnected are skipped. Live-only occurrences run only when Twitch confirms the stream is live; offline or unknown occurrences are not deferred until it becomes live.",
                "Stream uptime uses Twitch's current stream identity and start time. Discovering the stream at minute 45 fires a configured minute-30 threshold once. Repeated observations do not repeat that threshold. Recurring uptime intervals skip missed ticks; a disconnect is not a stream end.",
            ],
        };
        yield return new()
        {
            Heading = "Metadata and chat matching",
            Bullets =
            [
                "Channel metadata changed selects title changes, category changes or either, with an optional category identifier filter. The first observation is a baseline, not a change event.",
                "Chat matching supports case-insensitive whole-word keywords, literal contiguous phrases ignoring case, real Twitch emote identifiers, and a viewer's first observed message in the current stream. The keyword hi does not match this. Phrase matching is not regular-expression matching.",
                "Bot-authored output cannot trigger these matching sources. Shared-chat messages from another source channel do not become local matching events. Custom-command invocation remains a separate existing source.",
                "First observed means first seen by this bot during this actual stream, not Twitch's lifetime first-time chatter flag. The stream and seen viewer IDs survive bot restart; the seen list stores no message text. A confirmed stream end or genuinely new stream clears eligibility. An unseen viewer remains eligible after restart.",
            ],
        };
        yield return new()
        {
            Heading = "Committed feature events and cue playback",
            Bullets =
            [
                "Giveaway lifecycle reports a committed opening, closing or selected winners while Points is enabled. Guessing lifecycle reports a committed start or declared result while Guessing is enabled; merely closing answers is not a declared finish.",
                "Queue lifecycle reports a committed join or call to the next participants while Play with viewers is enabled. It does not expose private queue fields or participant notes. Public winner data follows the giveaway's existing publishing policy.",
                "Cue lifecycle selects Queued, Started, Finished or Interrupted, with optional cue and Cue player filters. It carries the actual run, cue and target identities while Overlays is enabled. Preview and draft tests do not become real cue lifecycle events.",
                "Cue outcomes distinguish queued work, server-started-unconfirmed playback, time-derived-end-unconfirmed completion, browser-reported-end-unverified completion and interruption. A connected Browser Source or browser completion report is not a verified rendering or exactly-once playback acknowledgement. Expired, cancelled, unavailable and interrupted work is not labelled successful playback.",
            ],
        };
        yield return new()
        {
            Heading = "Raids, goals, redemptions and moderation",
            Bullets =
            [
                "Outgoing raid observes the actual outgoing channel.raid result while Raid & collaboration is enabled. It is distinct from incoming raids and from channel.moderate raid action metadata; initiating or cancelling a raid is not proof that its raid event appeared in chat.",
                "Twitch goal selects Started, Progressed, Ended or a configured milestone, using channel:read:goals. The first or reconnected snapshot establishes a baseline without milestone catch-up. An observed crossing fires once per goal and milestone: 99→100→99→100 does not repeat it. A genuinely new goal has new eligibility.",
                "Redemption updated observes Fulfilled or Cancelled status after the existing redemption owner handles the update. It is separate from a new redemption and does not publish the viewer's private reward input. Rewards & redemptions must be enabled.",
                "Chat settings changed observes the actual settings update: slow mode and duration, subscriber/emote/follower modes and follower duration, and unique chat. It uses the configured bot's existing chat observation authority, not a new settings-management permission.",
                "Moderation action supports ban, timeout, unban, untimeout, clear, delete, emoteonly/emoteonlyoff, followers/followersoff, uniquechat/uniquechatoff, slow/slowoff, subscribers/subscribersoff, warn, mod/unmod, vip/unvip, raid/unraid, add/remove blocked or permitted terms, approve/deny unban requests, and shared_chat_ban, shared_chat_timeout, shared_chat_unban, shared_chat_untimeout and shared_chat_delete.",
                "Moderation uses channel.moderate version 2 and minimum observation approvals: moderator:read:blocked_terms, moderator:read:chat_settings, moderator:read:unban_requests, moderator:read:banned_users, moderator:read:chat_messages, moderator:read:warnings, moderator:read:moderators and moderator:read:vips. Twitch accepts existing manage alternatives for the first six; BlokeBot requests read approvals, not management power. Moderator and VIP read approvals remain required.",
                "Safe moderation outputs include the action, actual source-channel identity, subject's public name/login, applicable duration/count and moderator actor. Reasons, deleted message bodies, raw terms, private unban requests, warning rules and moderator messages are not automation outputs. Shared-chat source identity is retained; an observation for another subscribed host cannot invoke this host's sources.",
            ],
        };
        yield return new()
        {
            Heading = "Readiness, disabled work and repeated observations",
            Paragraphs =
            [
                "Configure these sources and countdown actions through the existing Toolbox and inspector. The events page reports missing approvals, disconnected observation and unavailable ad/stream evidence separately. Reconnect offers additional read approvals only for selected sources in enabled flows.",
                "Automations and each source's parent feature gate discovery, subscriptions, observation, admission and downstream actions. Disabling preserves saved configuration and history but suppresses new work; re-enabling does not replay old deliveries or skipped schedules. Already admitted Delay runs retain their existing recovery rules.",
                "Each logical source has one owner and route. Exact subscription requirements are reconciled, and a repeated provider occurrence cannot invoke the same source node twice. This is not cross-provider exactly-once delivery: deliberately distinct moderation-action and outgoing-result or settings-state nodes can each run for their distinct observations. Moderation does not synthesize an extra raid or settings event, and no semantic correlation between unrelated provider notifications is guessed.",
            ],
            Links =
            [
                new(
                    "Twitch EventSub subscriptions",
                    "https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/"
                ),
                new(
                    "Twitch moderation v2 payload",
                    "https://dev.twitch.tv/docs/eventsub/eventsub-reference/#channel-moderate-event-v2"
                ),
                new(
                    "Twitch ad schedule",
                    "https://dev.twitch.tv/docs/api/reference/#get-ad-schedule"
                ),
            ],
        };
    }
}
