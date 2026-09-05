namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityIdentityPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/moments",
            Eyebrow = "Community interaction · Moments",
            Title = "Channel Moments",
            Summary =
                "Turn live viewer calls into one moderated Twitch clip or marker. Then publish safe stream and weekly recaps.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-moments.png",
                LightPhoneSource: "media/community/phone-light-moments.png",
                DarkLaptopSource: "media/community/laptop-dark-moments.png",
                LightLaptopSource: "media/community/laptop-light-moments.png",
                PhoneAlt: "The Sample Channel stream recap on a narrow screen with an approved Community clutch save and a recorded vote.",
                LaptopAlt: "The Sample Channel Moments moderator page with capture settings and an approved Community clutch save in the clip gallery.",
                "Moderators work at /moments. Approved entries appear in channel and stream recaps. They also appear in weekly recaps."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a live channel",
                    Bullets =
                    [
                        "Select the channel.",
                        "Open Moments at /moments. Captures require a Twitch-reported live channel and the selected channel's Twitch connection.",
                        "Set the Merge window from 15 to 300 seconds. 90 seconds is the default. Calls inside that window join the same stream moment and keep each contributor and suggestion.",
                        "Select one reward option:",
                        "Reward option: No reward.",
                        "Reward option: First viewer to request.",
                        "Reward option: All contributing viewers.",
                        "Set the amount. Choose whether a confirmed clip failure can use a stream marker.",
                        "Save settings.",
                        "Check that the page shows Live stream with a stream identity.",
                        "Invite viewers to capture.",
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
                    Bullets =
                    [
                        "Capture state: Creating clip.",
                        "Capture state: Clip ready.",
                        "Capture state: Marker ready.",
                        "Capture state: Could not create clip.",
                        "Public recap field: approved title.",
                        "Public recap field: category.",
                        "Public recap field: counts.",
                        "Public recap field: Twitch link.",
                        "The moderator view retains Moderator note.",
                        "The moderator view retains rejection reason.",
                        "The moderator view retains audit text.",
                        "The moderator view retains Twitch failure details.",
                    ],
                    Heading = "Moderate public metadata",
                    Steps =
                    [
                        "In Candidates, review the applicable capture state.",
                        "Review the contributor count and viewer suggestions.",
                        "Set Public title and Category.",
                        "Select Save details.",
                        "Select Approve.",
                        "Use Open on Twitch to check available media.",
                    ],
                    Paragraphs =
                    [
                        "Public recaps show only the listed public recap fields.",
                        "Reject keeps its reason private. Merge uses another moment number.",
                        "Only approved moments appear in public recaps.",
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
                        PhoneAlt: "The Sample Channel public bounty shows an attached approved Moment on a narrow screen. Its public-safe title and Twitch media link are visible.",
                        LaptopAlt: "The Sample Channel public bounty shows an attached approved Moment on a narrow screen. Its public-safe title and Twitch media link are visible.",
                        "Authorized staff attach by reference in the destination. Viewers receive only the Moment's current approved public-safe fields."
                    ),
                    Paragraphs =
                    [
                        "The destination context stays visible. This context distinguishes the confirmed result from another match.",
                        "Detachment neither copies nor deletes these records.",
                    ],
                    Steps =
                    [
                        "Approve the Moment for the selected channel first.",
                        "As the channel owner or permitted moderator, open the destination for the attachment.",
                        "Open the destination's Moments section.",
                        "Select a same-host approved Moment.",
                        "Attach it.",
                        "Use Remove in the same section to detach the reference.",
                    ],
                    Bullets =
                    [
                        "Moments still owns the Moment.",
                        "Moments still owns its Twitch clip or marker.",
                        "Moments still owns the moderation history.",
                        "Attachment destination: a bounty.",
                        "Attachment destination: an achievement.",
                        "Attachment destination: a confirmed tournament match.",
                        "A bounty attachment inherits Moments.",
                        "A bounty attachment inherits Bounties.",
                        "A bounty attachment inherits the effective Points requirement for Bounties.",
                        "An achievement attachment inherits Moments and Community progression. A match attachment inherits Moments and Tournaments & leagues. This feature does not add an attachment switch.",
                        "Discovery requires all these conditions:",
                        "The Moment must be approved.",
                        "The Moment must belong to the same host.",
                        "The Moment must be currently public-safe.",
                        "BlokeBot suppresses unavailable Moments from management.",
                        "BlokeBot suppresses unavailable Moments from public destination pages.",
                        "BlokeBot suppresses unavailable Moments from events.",
                        "BlokeBot suppresses unavailable Moments from downstream presentation.",
                        "If the same source Moment returns to Approved, a retained link becomes visible again. Every parent gate must also be available. BlokeBot does not replay an attach event or suppressed work when it reappears.",
                        "Public destinations can show the current title.",
                        "Public destinations can show the current category.",
                        "Public destinations can show the current Twitch media link.",
                        "These Moment fields remain private: moderator notes and rejection reasons.",
                        "These Moment fields remain private: failure detail and internal IDs.",
                        "These Moment fields remain private: audit text.",
                        "If a parent is off, the embedded section shows Channel setup recovery.",
                        "If a parent is off, BlokeBot blocks discovery.",
                        "If a parent is off, BlokeBot blocks changes.",
                        "If a parent is off, BlokeBot blocks public relationships.",
                        "If a parent is off, BlokeBot blocks events.",
                        "If a parent is off, BlokeBot blocks overlays.",
                        "If a parent is off, BlokeBot blocks automations.",
                        "Valid links remain saved and reappear from current state after re-enable without replay.",
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
                        "If the state is Offline, wait for a live stream.",
                        "If Twitch reports that clips or VODs are disabled, correct that setting or continue with no marker fallback.",
                        "If access is unauthorized, reconnect the selected channel account.",
                        "If the failure continues, keep the candidate.",
                        "Include the channel in the information for the server owner.",
                        "Include the moment number in the information for the server owner.",
                        "Include the stream identity in the information for the server owner.",
                        "Include the approximate time in the information for the server owner.",
                        "Include the Twitch message in the information for the server owner.",
                        "Never send tokens or private moderation text.",
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
                    Bullets =
                    [
                        "Only the viewer can choose the profile line.",
                        "Only the viewer can choose the rewards.",
                        "Only the viewer can choose the visibility.",
                        "Only the viewer can choose the attendance choice.",
                    ],
                    Heading = "Enable Viewer passports",
                    Paragraphs =
                    [
                        "The feature card saves the change immediately. Each channel starts with this switch off.",
                        "BlokeBot links the passport to the signed-in Twitch user ID.",
                        "A later login or display-name change updates the same profile. A new passport starts Private and hides attendance.",
                    ],
                    Steps =
                    [
                        "As the channel owner or permitted moderator, select the channel.",
                        "Open Channel setup.",
                        "Turn on Viewer passports under Chat tools.",
                        "As the viewer, sign in with Twitch.",
                        "Open /passports/{channel}/me.",
                        "Save a different visibility to approve a broader audience.",
                    ],
                    Note = "Replace {channel} with the channel login.",
                },
                new SiteGuideSection
                {
                    Heading = "Profile content",
                    LegacyAnchor = "create-a-bounded-profile",
                    Bullets =
                    [
                        "Enter a profile line of 160 characters or fewer.",
                        "BlokeBot presents the profile line as plain text. The channel moderation policy still applies.",
                        "Choose only a title or badge that the viewer earned in this channel.",
                        "BlokeBot rejects an unearned or stale reward selection.",
                        "The passport preview includes permitted points and permitted rank.",
                        "The passport preview includes permitted Guessing results and permitted achievements.",
                        "The passport preview includes permitted game wins and permitted giveaway wins.",
                        "The passport preview includes supported bounties and approved Moments.",
                        "Each source feature remains authoritative. The passport summarizes its current records.",
                        "Attendance counts consecutive recorded streams with a chat message. It does not measure watch time or every broadcast.",
                        "Change Show attendance streak independently of profile visibility.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose the audience",
                    Bullets =
                    [
                        "Public lets anyone with /passport/{channel}/{viewer} see the selected public-safe fields.",
                        "Public passport visibility permits chat projections.",
                        "Public passport visibility permits overlay projections.",
                        "Public passport visibility permits automation projections.",
                        "Channel members permits the viewer.",
                        "Channel members permits channel managers.",
                        "Channel members permits signed-in people with a passport in this channel.",
                        "Other accounts receive an unavailable result.",
                        "Private permits only the listed Private audiences:",
                        "Private audience: the viewer.",
                        "Private audience: the channel owner.",
                        "Private audience: permitted managers.",
                        "Private and Channel members profiles stay out of all public identity projections.",
                        "The public route excludes Twitch user IDs and private source history.",
                        "The public route excludes hidden attendance and unselected rewards.",
                    ],
                    Code = "!passport",
                },
                new SiteGuideSection
                {
                    Heading = "Passport data",
                    LegacyAnchor = "save-export-or-reset-the-passport",
                    Bullets =
                    [
                        "Select Save passport after a profile change.",
                        "This control is the sticky Save action for the page.",
                        "Export my channel data downloads data that this BlokeBot installation associates with the viewer's Twitch identity in this channel.",
                        "Confirm Reset passport to remove the passport and its stream attendance.",
                        "The reset does not change original points records and Guessing records.",
                        "The reset does not change original achievement records and giveaway records.",
                        "The reset does not change original bounty records and Moment records.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Restore privacy and availability",
                    Bullets =
                    [
                        "If a public link is unavailable, check the channel and viewer login.",
                        "Check the visibility and audience access.",
                        "Check the passport state.",
                        "If activity is stale, use the current Twitch identity. Wait for a supported source event.",
                        "BlokeBot does not reconstruct suppressed or historical activity on demand.",
                        "Turn off Viewer passports to remove discovery and public output.",
                        "With Viewer passports off, BlokeBot blocks edits and chat updates before effects.",
                        "With Viewer passports off, BlokeBot blocks commands and exports before effects.",
                        "With Viewer passports off, BlokeBot blocks resets and overlay data before effects.",
                        "With Viewer passports off, BlokeBot blocks automation payloads before effects.",
                        "The signed-in direct route links to Channel setup.",
                        "BlokeBot keeps passports.",
                        "BlokeBot keeps visibility.",
                        "BlokeBot keeps stream attendance.",
                        "The next new stream attendance starts a new streak after you re-enable the feature.",
                        "BlokeBot does not replay suppressed chat messages and events.",
                        "BlokeBot does not replay suppressed timers and queued work.",
                        "BlokeBot does not replay suppressed provider actions.",
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
