namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityIdentityPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/moments",
            Eyebrow = "Community interaction · Moments",
            Title = "Capture, moderate and recap moments",
            Summary =
                "Turn live viewer calls into one moderated Twitch clip or marker, then publish safe stream and weekly recaps.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-moments.png",
                LightPhoneSource: "media/community/phone-light-moments.png",
                DarkLaptopSource: "media/community/laptop-dark-moments.png",
                LightLaptopSource: "media/community/laptop-light-moments.png",
                PhoneAlt: "The Sample Channel stream recap on a narrow screen with an approved Community clutch save and a recorded vote.",
                LaptopAlt: "The Sample Channel Moments moderator page with capture settings and an approved Community clutch save in the clip gallery.",
                "Moderators work at /moments. Approved entries appear in channel, stream and weekly recaps."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a live channel",
                    Bullets =
                    [
                        "Choose the channel and open Moments at /moments. Captures require Twitch to report that channel live and require the selected channel's Twitch connection.",
                        "Set the Merge window from 15 to 300 seconds. 90 seconds is the default. Calls inside that window join the same stream moment and keep each contributor and suggestion.",
                        "Choose No reward, First viewer to request or All contributing viewers. Set the amount. Choose whether a confirmed clip failure can use a stream marker.",
                        "Save settings and check that the page shows Live stream with a stream identity. Then invite viewers to capture.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Capture a candidate",
                    Bullets =
                    [
                        "A viewer uses !moment <suggested title> | category=<suggested category>. !clip accepts the same form.",
                        "A moderator can use Capture now. BlokeBot first requests a Twitch clip. BlokeBot uses marker fallback only after a confirmed clip failure and only when you enable it.",
                        "Each call returns a public moment number. Repeated or concurrent calls for the same live moment converge. They do not create duplicate Twitch actions or rewards.",
                    ],
                    Note =
                        "BlokeBot links to Twitch media. It does not copy or host the clip or VOD.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate public metadata",
                    Steps =
                    [
                        "In Candidates, review Creating clip, Clip ready, Marker ready or Could not create clip. Review the contributor count and viewer suggestions.",
                        "Set Public title and Category, select Save details, then Approve. Reject keeps its reason private. Merge uses another moment number.",
                        "Use Open on Twitch to verify available media. Only approved moments appear in public recaps.",
                    ],
                    Paragraphs =
                    [
                        "Moderator note, rejection reason, audit text and Twitch failure details stay on the moderator view. Public recaps show only approved title, category, counts and the Twitch link.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Attach approved Moments to progression",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/figures/phone-dark-moment-attachment.png",
                        LightPhoneSource: "media/community/figures/phone-light-moment-attachment.png",
                        DarkLaptopSource: "media/community/figures/laptop-dark-moment-attachment.png",
                        LightLaptopSource: "media/community/figures/laptop-light-moment-attachment.png",
                        PhoneAlt: "The Sample Channel public bounty with an attached approved Moment, public-safe title and Twitch media link on a narrow screen.",
                        LaptopAlt: "The Sample Channel public bounty with an attached approved Moment, public-safe title and Twitch media link on a narrow screen.",
                        "Authorized staff attach by reference in the destination. Viewers receive only the Moment's current approved public-safe fields."
                    ),
                    Steps =
                    [
                        "Approve the Moment for the selected channel first. A channel owner or permitted moderator then opens the destination bounty, achievement or confirmed tournament match.",
                        "Open its Moments section, choose a same-host approved Moment and attach it. The destination context remains visible. It prevents confusion between a confirmed result and another match.",
                        "Use Remove in the same section to detach the reference. The Moment, Twitch clip or marker and moderation history remain owned by Moments and are not copied or deleted.",
                    ],
                    Bullets =
                    [
                        "A bounty attachment inherits Moments, Bounties and Bounties' effective Points requirement. An achievement attachment inherits Moments and Community progression. A match attachment inherits Moments and Tournaments & leagues. This feature does not add an attachment switch.",
                        "Only approved, same-host, currently public-safe Moments are discoverable. BlokeBot suppresses unavailable Moments from management, public destination pages, events and downstream presentation.",
                        "If the same source Moment returns to Approved, a retained link becomes visible again. Every parent gate must also be available. BlokeBot does not replay an attach event or suppressed work when it reappears.",
                        "Public destinations can show current title, category and Twitch media link. Moderator notes, rejection reasons, failure detail, internal IDs and audit text remain private.",
                        "If a parent is off, the embedded section shows Channel setup recovery. It blocks discovery, changes, public relationships, events, overlays and automations. Valid links remain saved and reappear from current state after re-enable without replay.",
                    ],
                    Links =
                    [
                        new SiteLink("Manage viewer-funded bounties", "community/bounties"),
                        new SiteLink("Manage achievements", "community/progression"),
                        new SiteLink("Manage confirmed matches", "community/competitions"),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Share recaps and votes",
                    Bullets =
                    [
                        "Weekly recap opens /moments/{channel} for the current ISO-UTC week. A stream recap uses /moments/{channel}/streams/{stream-id}.",
                        "A signed-in viewer votes with Twitch ID. An unsigned viewer can enter a normalized Twitch login. Each identity contributes at most one vote to a moment.",
                        "Finalize previous week records the winner for the completed week. It uses the vote count and stable order. A repeated finalization returns the same winner.",
                    ],
                    Note =
                        "Replace every value in braces with the channel login or Twitch stream identity shown by BlokeBot. Do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Read Twitch states before you retry",
                    Bullets =
                    [
                        "Creating clip means that Twitch continues to prepare the clip. Reload the same candidate later. Do not capture again just to force an answer.",
                        "An ambiguous outcome means Twitch did not confirm whether its request completed. BlokeBot preserves that uncertainty and does not create a fallback marker from it.",
                        "Offline means wait for a live stream. If Twitch reports that clips or VODs are disabled, correct that setting or continue with no marker fallback. If access is unauthorized, reconnect the selected channel account.",
                        "If the failure continues, keep the candidate. Send the channel, moment number, stream identity, approximate time and Twitch message to the server owner. Never send tokens or private moderation text.",
                    ],
                },
            ],
            Next = [new SiteLink("Use Native Twitch tools", "twitch-operations")],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/passports",
            Eyebrow = "Community interaction · Viewer identity",
            Title = "Choose a viewer passport",
            Summary =
                "Create a host-scoped profile. Choose its audience and the activity that BlokeBot presents.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-viewer-passport-participant.png",
                LightPhoneSource: "media/community/figures/phone-light-viewer-passport-participant.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-viewer-passport-participant.png",
                LightLaptopSource: "media/community/figures/laptop-light-viewer-passport-participant.png",
                PhoneAlt: "The Sample Channel public NightOwl viewer passport on a narrow screen that shows selected public identity and channel activity.",
                LaptopAlt: "The Sample Channel public NightOwl viewer passport on a narrow screen that shows selected public identity and channel activity.",
                "A viewer controls the editor and visibility. The public route contains only the permitted projection."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable Viewer passports",
                    Steps =
                    [
                        "As the channel owner or permitted moderator, choose the channel. Open Channel setup. Turn on Viewer passports under Chat tools.",
                        "Expect the feature card to save the change immediately. Expect each channel to start with this switch off.",
                        "As the viewer, sign in with Twitch. Open /passports/{channel}/me. Expect BlokeBot to link the passport to that Twitch user ID.",
                        "Expect a later login or display-name change to update the same profile. Expect a new passport to start Private and hide attendance.",
                        "Save a different visibility to approve a broader audience.",
                    ],
                    Note =
                        "Replace {channel} with the channel login. Only the viewer can choose the profile line, rewards, visibility, and attendance choice.",
                },
                new SiteGuideSection
                {
                    Heading = "Create a bounded profile",
                    Bullets =
                    [
                        "Enter a profile line of 160 characters or fewer.",
                        "BlokeBot presents the profile line as plain text. The channel moderation policy still applies.",
                        "Choose only a title or badge that the viewer earned in this channel.",
                        "BlokeBot rejects an unearned or stale reward selection.",
                        "The preview combines permitted points, rank, Guessing results, achievements, game and giveaway wins, supported bounties, and approved Moments. Each source feature remains authoritative. The passport summarizes its current records.",
                        "Attendance counts consecutive recorded streams with a chat message. It does not measure watch time or every broadcast.",
                        "Change Show attendance streak independently of profile visibility.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose the audience",
                    Bullets =
                    [
                        "Public lets anyone with /passport/{channel}/{viewer} see the selected public-safe fields. It also permits chat, overlay, and automation projections.",
                        "Channel members permits the viewer, channel managers, and signed-in people with a passport in this channel. Other accounts receive an unavailable result.",
                        "Private permits only the viewer, channel owner, and permitted managers. Private and Channel members profiles stay out of all public identity projections.",
                        "The public route excludes Twitch user IDs, private source history, hidden attendance, and unselected rewards.",
                    ],
                    Code = "!passport",
                },
                new SiteGuideSection
                {
                    Heading = "Save, export, or reset the passport",
                    Bullets =
                    [
                        "Select Save passport after a profile change.",
                        "This control is the sticky Save action for the page.",
                        "Export my channel data downloads data that this BlokeBot installation associates with the viewer's Twitch identity in this channel.",
                        "Confirm Reset passport to remove the passport and its stream attendance.",
                        "The reset does not change original points, Guessing, achievement, giveaway, bounty, or Moment records.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Restore privacy and availability",
                    Bullets =
                    [
                        "If a public link is unavailable, verify the channel, viewer login, visibility, audience access, and passport state.",
                        "If activity is stale, use the current Twitch identity and wait for a supported source event.",
                        "BlokeBot does not reconstruct suppressed or historical activity on demand.",
                        "Turn off Viewer passports to remove discovery and public output.",
                        "BlokeBot then blocks edits, chat updates, commands, exports, resets, overlay data, and automation payloads before effects.",
                        "The signed-in direct route links to Channel setup. BlokeBot keeps passports, visibility, and stream attendance.",
                        "The next new stream attendance starts a new streak after you re-enable the feature.",
                        "BlokeBot does not replay suppressed chat messages, events, timers, queued work, or provider actions.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Create seasons and earned rewards", "community/progression"),
                new SiteLink("Review privacy boundaries", "privacy"),
            ],
        };
    }
}
