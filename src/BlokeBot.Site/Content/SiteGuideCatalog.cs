namespace BlokeBot.Site.Content;

internal static class SiteGuideCatalog
{
    private static readonly IReadOnlyDictionary<string, SiteGuidePage> _pages = CreatePages()
        .ToDictionary(page => page.Route, StringComparer.Ordinal);

    internal static IReadOnlyList<SiteGuidePage> All { get; } =
        SiteRoutes.GuideTopics.Select(route => _pages[route]).ToArray();

    internal static IReadOnlyList<SiteGuideNavigationGroup> NavigationGroups { get; } =
    [
        new(
            "Start and setup",
            [
                GuideLink("Getting started", "guide/getting-started"),
                GuideLink("Dashboard", "dashboard"),
                GuideLink("Channels", "channels"),
                GuideLink("Twitch connections", "connect"),
                GuideLink("Channel tools", "tools"),
            ]
        ),
        new(
            "Community interaction",
            [
                GuideLink("Request boards", "community/request-boards"),
                GuideLink("Play with viewers", "community/play-with-viewers"),
                GuideLink("Moments", "community/moments"),
            ]
        ),
        new(
            "Native Twitch",
            [
                GuideLink("Overview", "twitch-operations"),
                GuideLink("Shoutouts", "twitch-operations/shoutouts"),
                GuideLink("Polls", "twitch-operations/polls"),
                GuideLink("Clips and markers", "twitch-operations/clips-markers"),
                GuideLink("Rewards and redemptions", "twitch-operations/channel-points"),
                GuideLink("Predictions", "twitch-operations/predictions"),
            ]
        ),
        new(
            "Chat, games and points",
            [
                GuideLink("Commands", "commands"),
                GuideLink("Guessing games", "guessing"),
                GuideLink("Viewer points", "points"),
                GuideLink("Giveaways", "giveaways"),
                GuideLink("Leaderboards", "leaderboards"),
            ]
        ),
        new(
            "Help and administration",
            [
                GuideLink("Troubleshooting", "troubleshooting"),
                GuideLink("Moderator access", "moderators"),
                GuideLink("Server owners", "server-owners"),
            ]
        ),
    ];

    internal static SiteGuidePage Get(string route)
    {
        return _pages.TryGetValue(route, out var page)
            ? page
            : throw new InvalidOperationException($"No guide content is registered for '{route}'.");
    }

    private static SiteLink GuideLink(string label, string href)
    {
        _ = Get($"/{href}");
        return new(label, href);
    }

    private static IEnumerable<SiteGuidePage> CreatePages()
    {
        yield return new SiteGuidePage
        {
            Route = "/guide/getting-started",
            Eyebrow = "Start here",
            Title = "Sign in and choose your channel",
            Summary =
                "Use your normal Twitch account, then choose the channel whose tools you want to view or manage.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Before you begin",
                    Bullets =
                    [
                        "Have the BlokeBot web address you were given.",
                        "Use the Twitch account connected to your channel or moderator role.",
                        "Ask a channel owner or BlokeBot administrator if you need permission to change setup.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Sign in",
                    Steps =
                    [
                        "Open the BlokeBot address and select Continue with Twitch.",
                        "Sign in to Twitch and review the permissions Twitch shows.",
                        "Return to BlokeBot and check your account name and role in the top bar.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose a channel",
                    Steps =
                    [
                        "Select My channel for the Twitch channel you own.",
                        "Use Other channels when you help manage another available channel.",
                        "Select Find channels again if a newly available channel is missing.",
                    ],
                    Paragraphs =
                    [
                        "If you cannot create a channel setup, ask a BlokeBot administrator to approve you or add the channel.",
                    ],
                },
            ],
            Next = [new SiteLink("Learn the dashboard", "dashboard")],
        };

        yield return new SiteGuidePage
        {
            Route = "/dashboard",
            Eyebrow = "Everyday navigation",
            Title = "Find your way around the dashboard",
            Summary =
                "The navigation follows the selected channel, groups its tools by task and shows only the features that channel has turned on.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-home.png",
                LightPhoneSource: "media/phone-light-home.png",
                DarkLaptopSource: "media/laptop-dark-home.png",
                LightLaptopSource: "media/laptop-light-home.png",
                PhoneAlt: "BlokeBot dashboard showing the selected Sample Channel, channel setup and chat-tool navigation.",
                LaptopAlt: "BlokeBot dashboard showing the selected Sample Channel, channel setup and chat-tool navigation.",
                "The selected channel appears in the top bar; its enabled tools appear in the menu."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Check the top bar first",
                    Bullets =
                    [
                        "Bot status shows whether the selected channel is ready or needs attention.",
                        "My channel and Other channels change which channel you are working on.",
                        "Alerts opens current problems; the account menu shows your role and Sign out.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Use the menu",
                    Bullets =
                    [
                        "Home gives a short introduction and public leaderboard shortcut.",
                        "Channel setup contains connections, moderator access and feature switches.",
                        "Chat tools contains Request boards, Play with viewers and Moments for the selected channel, plus each enabled Native Twitch, Guessing, Points and Custom commands feature.",
                        "Expand Native Twitch to move between its five focused task pages.",
                    ],
                    Paragraphs =
                    [
                        "Always confirm the selected channel before saving. A change for one channel does not change another.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Manage channels and access", "channels"),
                new SiteLink("Connect the bot", "connect"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/channels",
            Eyebrow = "Channels and access",
            Title = "Add, choose and manage channels",
            Summary =
                "Each Twitch channel keeps its own connection, tools, games, points and people who can help.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-channel-setup.png",
                LightPhoneSource: "media/phone-light-channel-setup.png",
                DarkLaptopSource: "media/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup for Sample Channel showing Twitch chat, readiness and bot status panels.",
                LaptopAlt: "Channel setup for Sample Channel showing Twitch chat, readiness and bot status panels.",
                "The selected channel appears in the top bar; its enabled tools appear in the menu."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create your channel setup",
                    Steps =
                    [
                        "Sign in with the Twitch account that owns the channel.",
                        "Select My channel, open Channel setup and choose Create channel setup.",
                        "If the action is unavailable, ask a BlokeBot administrator for channel-creation access.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Switch safely",
                    Paragraphs =
                    [
                        "Use the channel selector whenever you help more than one channel. You may be allowed to use a channel's tools without permission to change its setup.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Connect this channel", "connect"),
                new SiteLink("Let moderators help", "moderators"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/connect",
            Eyebrow = "Twitch connection",
            Title = "Connect the bot to your channel",
            Summary =
                "BlokeBot explains which Twitch account or permission is needed and keeps the bot stopped until the channel is ready.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-channel-setup.png",
                LightPhoneSource: "media/phone-light-channel-setup.png",
                DarkLaptopSource: "media/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup showing a Connect channel action beside Twitch chat and an offline bot status.",
                LaptopAlt: "Channel setup showing a Connect channel action beside Twitch chat and an offline bot status.",
                "Connection actions and readiness messages appear beside the channel's bot controls."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Connect the channel account",
                    Steps =
                    [
                        "Select the channel and open Channel setup.",
                        "Under Twitch chat, select Connect channel.",
                        "Complete Twitch as the channel owner. This grants the channel-level permission used by the bot.",
                        "Return to the same selected channel and confirm that the channel connection is ready.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Connect the bot account",
                    Steps =
                    [
                        "Sign out of Twitch in the connection pop-up if it is using your normal account.",
                        "Select Connect bot and sign in as the dedicated bot account named by BlokeBot.",
                        "Make the bot a moderator in your Twitch channel. This is the recommended setup for announcements and follower-only chat.",
                        "Select Start bot when the controls become available.",
                        "Use Stop bot when you intentionally want BlokeBot out of chat.",
                    ],
                    Note =
                        "Twitch does not provide an API that lets BlokeBot make its bot account follow your channel. If the channel uses follower-only chat and the bot is not a moderator, sign into Twitch as the bot and follow the channel manually. BlokeBot checks this state and alerts when follower-only delivery is rejected.",
                },
                new SiteGuideSection
                {
                    Heading = "Reconnect the right identity",
                    Paragraphs =
                    [
                        "Use the reconnect action beside the connection that is stale. Channel and bot connections are different OAuth grants; reconnecting one does not repair the other.",
                        "If Twitch used the wrong account, close the result window, sign out of Twitch in that browser context and repeat the account-specific action.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Choose channel tools", "tools"),
                new SiteLink("Troubleshoot a connection", "troubleshooting"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/tools",
            Eyebrow = "Channel tools",
            Title = "Choose the tools your channel needs",
            Summary =
                "Use the selected channel's community tools, and turn Native Twitch, commands, guessing or points on independently.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn a tool on",
                    Steps =
                    [
                        "Choose the correct channel and open Channel setup.",
                        "Open Chat tools and turn on the feature you want.",
                        "Open the new navigation item and finish its settings before using it live.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "What you can add",
                    Links =
                    [
                        new SiteLink("Request boards", "community/request-boards"),
                        new SiteLink("Play with viewers", "community/play-with-viewers"),
                        new SiteLink("Moments and recaps", "community/moments"),
                        new SiteLink("Commands and scheduled messages", "commands"),
                        new SiteLink("Guessing games", "guessing"),
                        new SiteLink("Points", "points"),
                        new SiteLink("Giveaways", "giveaways"),
                        new SiteLink("Public leaderboards", "leaderboards"),
                        new SiteLink("Native Twitch", "twitch-operations"),
                    ],
                },
            ],
            Next = [new SiteLink("Set up a request board", "community/request-boards")],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/request-boards",
            Eyebrow = "Community interaction · Requests",
            Title = "Run a structured request board",
            Summary =
                "Collect consistent viewer suggestions, moderate their lifecycle and keep point charges and public status understandable.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/request-boards-participant-mobile.png",
                LightPhoneSource: "media/community/request-boards-participant-mobile.png",
                DarkLaptopSource: "media/community/request-boards-moderator-desktop.png",
                LightLaptopSource: "media/community/request-boards-moderator-desktop.png",
                PhoneAlt: "The Sample Channel public request board on a narrow screen, showing open rules and the start of the submission form.",
                LaptopAlt: "The Sample Channel Request boards moderator page, showing a saved Game night requests board and its configuration.",
                "Moderators configure the board at /requests; viewers use its public channel-and-board address."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose the right view",
                    Bullets =
                    [
                        "A channel owner or permitted moderator chooses the channel and opens Request boards at /requests to create, configure and moderate boards.",
                        "Open public board copies the viewer route /requests/{channel}/{board-name}. Anyone can read an existing board; a viewer signs in with Twitch to submit, vote or withdraw.",
                        "Chat participants can discover boards with !requests. Website and chat actions use the same board, limits, votes and request states.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the board's Command and URL name; do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Configure a board",
                    Steps =
                    [
                        "Select New, give the board a Command and URL name, title and description, then choose whether it accepts submissions.",
                        "Set the point cost, refund policy, active-submission limit, submission cooldown, voting switch and per-viewer vote limit.",
                        "Add only the fields participants need. A field can be Text, URL, Twitch clip, Number or Choice; set its label, required state and applicable length, choice or number limits.",
                        "Select Save board, then use Open public board and read the Board rules exactly as a participant will see them.",
                    ],
                    Paragraphs =
                    [
                        "The public queue order explains the complete stable ordering. Moderator priority, votes and assigned queue position refine that order without hiding a different participant rule.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Submit and vote",
                    Bullets =
                    [
                        "On the public page, sign in with Twitch, complete Title and the configured fields, then select Submit request. The page shows the request ID and its current public state.",
                        "In chat, use !request <board> <title> | field=value | category=value | tags=a,b. Required field keys come from that board's configuration.",
                        "Use !requestvote <request-id> to vote in chat, or Vote on the public board. Repeating the same vote does not add another vote.",
                        "A submitter can Withdraw an active request from the public page. Private moderator text is never shown there.",
                    ],
                    Note =
                        "A repeated delivery of the same chat submission is recognised and reports the existing request instead of creating or charging a second one.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate the lifecycle",
                    Steps =
                    [
                        "Review the submitted values and any possible-duplicate warning. Set public category, tags, priority and Public note when they help participants.",
                        "Move Pending to Approved or Rejected. Approved requests can move to Queued or Accepted; Queued or Accepted requests can move to Completed.",
                        "Use Merge with the target request ID when two entries are the same request. The public board keeps the merged outcome and the surviving request's combined support.",
                        "When the dashboard is not convenient, use !requestapprove, !requestreject, !requestqueue, !requestaccept or !requestcomplete followed by one request ID.",
                        "To merge in chat, use !requestmerge <source-id> <target-id>.",
                    ],
                    Paragraphs =
                    [
                        "Private moderator note and Private rejection reason remain moderator-only. Put participant-facing context in Public note instead.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Points, failure and recovery",
                    Bullets =
                    [
                        "A non-zero cost is reserved and deducted from the viewer's available balance when the board accepts the initial submission, before moderator review. It remains reserved through Pending, Approved, Queued and Accepted. Never manually charge the viewer as well.",
                        "Completion consumes the reservation. A closure refunds it only under the configured policy: Never does not refund; Rejected or withdrawn refunds those two closures; Any unfulfilled closure also refunds other closures that did not complete.",
                        "If validation, the cooldown, a limit or the balance rejects a submission, correct the message shown and submit once. If an outcome is already visible, reload before trying again.",
                        "If request state and points still disagree after reload, leave the request unchanged and send the channel, board name, request ID, approximate time and visible message to the server owner. Do not share Twitch tokens or private notes.",
                    ],
                },
            ],
            Next = [new SiteLink("Build a play-with-viewers queue", "community/play-with-viewers")],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/play-with-viewers",
            Eyebrow = "Community interaction · Queues",
            Title = "Build fair play-with-viewers parties",
            Summary =
                "Open a queue, collect private entry details, run ready checks and deliver lobby information without posting it publicly.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/play-with-viewers-participant-mobile.png",
                LightPhoneSource: "media/community/play-with-viewers-participant-mobile.png",
                DarkLaptopSource: "media/community/play-with-viewers-moderator-desktop.png",
                LightLaptopSource: "media/community/play-with-viewers-moderator-desktop.png",
                PhoneAlt: "The Sample Channel Community night party viewer page on a narrow screen, showing the public queue rule and private entry form.",
                LaptopAlt: "The Sample Channel Play with viewers moderator page, showing a saved queue, party size and fair-selection configuration.",
                "The moderator route /queues and viewer route /queues/{channel}/{queue-name} share one live queue."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose identities and permissions",
                    Bullets =
                    [
                        "A channel owner or permitted moderator chooses the channel and opens Play with viewers at /queues.",
                        "Open viewer page uses /queues/{channel}/{queue-name}. Signed-in participants use their Twitch identity; an unsigned participant can enter a Twitch login as the bounded fallback.",
                        "Moderator controls, entry answers, priorities, private notes and lobby messages are never shown on the public page.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the queue's Command and URL name; do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Configure and open the queue",
                    Steps =
                    [
                        "Select New, set the Command and URL name, Queue name, Game or activity and Party size.",
                        "Choose Join order or Least recent participation. The viewer page states the resulting fair-selection rule before anyone joins.",
                        "Set Ready expiry, History retention and Skip/no-show exclusion. Add private entry fields and any required roles in role=count form.",
                        "Decide whether participant names may be shown publicly, turn Queue open on, save, then inspect Open viewer page at both wide and narrow widths.",
                    ],
                    Paragraphs =
                    [
                        "Platform, region, rank, preferred role and every custom entry field are private to moderators even when public participant names are enabled.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Join from the page or chat",
                    Bullets =
                    [
                        "On the viewer page, fill the requested fields and select Join. Check position reports the current place; Leave removes the entry; I'm ready answers an active ready check.",
                        "In chat use !queue [queue], !join [queue] key=value, !leave [queue], !position [queue] and !ready [queue]. The queue name is optional when the channel has only one queue.",
                        "Joining twice keeps one entry. Signed-in Twitch ID is authoritative; normalized-login fallback lets an unsigned viewer participate without creating duplicate public names.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Select and run a party",
                    Steps =
                    [
                        "Review Waiting viewers and the visible next-candidate order. Adjust Priority or Private moderator note only when a documented channel rule requires it.",
                        "Start a Ready check for candidates. Participants must use I'm ready or !ready before Ready expiry; then select Select next party.",
                        "Use Keep party to retain the current group, Replace one for a single change, or Skip and No-show when someone cannot play. The configured exclusion prevents immediate re-entry after a skip or no-show.",
                        "Enter the Private lobby message and select Whisper party. Confirm success before starting; never paste a private lobby code into public chat as a fallback.",
                    ],
                    Paragraphs =
                    [
                        "Moderators can use !queueopen [queue] and !queueclose [queue]. Close the queue while resolving a disputed selection so new joins do not move the visible candidate order.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover safely",
                    Bullets =
                    [
                        "If a participant misses Ready expiry, run a new ready check or use Replace one. Use No-show only when the channel's exclusion rule should apply.",
                        "If a whisper fails, verify that the bot connection can whisper and retry Whisper party only after the page reports the failure. Do not reveal the private message publicly.",
                        "If selection cannot satisfy required roles, leave the current party intact, adjust the waiting pool or role requirements and select again.",
                        "History retention removes old participation data after the configured period. Shortening it changes future fairness evidence, so record that channel decision before saving.",
                    ],
                },
            ],
            Next = [new SiteLink("Capture and recap community moments", "community/moments")],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/moments",
            Eyebrow = "Community interaction · Moments",
            Title = "Capture, moderate and recap moments",
            Summary =
                "Turn live viewer calls into one moderated Twitch clip or marker, then publish safe stream and weekly recaps.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/moments-participant-mobile.png",
                LightPhoneSource: "media/community/moments-participant-mobile.png",
                DarkLaptopSource: "media/community/moments-moderator-desktop.png",
                LightLaptopSource: "media/community/moments-moderator-desktop.png",
                PhoneAlt: "The Sample Channel stream recap on a narrow screen, showing an approved Community clutch save and a recorded vote.",
                LaptopAlt: "The Sample Channel Moments moderator page, showing live capture settings and an approved Community clutch save.",
                "Moderators work at /moments; approved entries appear in channel, stream and weekly recaps."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a live channel",
                    Bullets =
                    [
                        "Choose the channel and open Moments at /moments. Captures require Twitch to report that channel live and require the selected channel's Twitch connection.",
                        "Set the Merge window from 15 to 300 seconds; 90 seconds is the default. Calls inside that window join the same stream moment and keep each contributor and suggestion.",
                        "Choose no point reward, First requester or All contributors, set the amount, and decide whether a confirmed clip failure may fall back to a stream marker.",
                        "Save settings and check that the page shows Live stream with a stream identity before inviting viewers to capture.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Capture a candidate",
                    Bullets =
                    [
                        "A viewer uses !moment <suggested title> | category=<suggested category>. !clip accepts the same form.",
                        "A moderator can use Capture now. BlokeBot first requests a Twitch clip; marker fallback is used only after a confirmed clip failure and only when enabled.",
                        "Each call returns a public moment ID. Repeated or concurrent calls for the same live moment converge instead of creating duplicate provider actions or duplicate rewards.",
                    ],
                    Note =
                        "BlokeBot links to Twitch media; it does not copy or host the clip or VOD.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate public metadata",
                    Steps =
                    [
                        "Review the provider state, contributor count and viewer suggestions in Candidates.",
                        "Set Public title and Category, then Approve. Save metadata updates an existing candidate; Reject keeps its reason private; Merge uses another moment's public ID.",
                        "Use Open on Twitch to verify available media. Only approved moments appear in public recaps.",
                    ],
                    Paragraphs =
                    [
                        "Private moderator note, rejection reason, audit text and provider failure details stay on the moderator view. Public recaps show only approved title, category, counts and the Twitch link.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Share recaps and votes",
                    Bullets =
                    [
                        "Weekly recap opens /moments/{channel} for the current ISO-UTC week. A stream recap uses /moments/{channel}/streams/{stream-id}.",
                        "A signed-in viewer votes with Twitch ID. An unsigned viewer may enter a normalized Twitch login. Each identity contributes at most one vote to a moment.",
                        "Finalize previous week records the winner for the completed week using vote count and stable ordering. Repeating finalization returns the same winner.",
                    ],
                    Note =
                        "Replace every value in braces with the channel login or Twitch stream identity shown by BlokeBot; do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Read provider states before retrying",
                    Bullets =
                    [
                        "Provider pending means Twitch has not finished the clip. Reload the same candidate later; do not capture again just to force an answer.",
                        "An ambiguous outcome means Twitch did not confirm whether its request completed. BlokeBot preserves that uncertainty and does not create a fallback marker from it.",
                        "Offline means wait for a live stream. If Twitch reports clips or VODs disabled, correct that Twitch setting or continue without marker fallback. If access is unauthorized, reconnect the selected channel account.",
                        "For a continuing failure, keep the candidate and send the selected channel, public moment ID, stream identity, approximate time and visible provider message to the server owner. Never send tokens or private moderation text.",
                    ],
                },
            ],
            Next = [new SiteLink("Use Native Twitch operations", "twitch-operations")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations",
            Eyebrow = "Native Twitch",
            Title = "Use Twitch channel tools",
            Summary =
                "Send shoutouts, run polls, save live moments, manage rewards and settle Predictions for the selected channel.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn Native Twitch on",
                    Steps =
                    [
                        "Choose the channel in the top bar and open Channel setup.",
                        "Open Chat tools, turn on Native Twitch and save the change.",
                        "Open Native Twitch in the Chat tools navigation, then choose Shoutouts, Polls, Clips & markers, Rewards & redemptions or Predictions.",
                    ],
                    Paragraphs =
                    [
                        "Turning Native Twitch off hides these pages and stops its automatic work. Saved templates, settings and history remain for the next time you turn it on.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Follow the action on the page",
                    Bullets =
                    [
                        "Shoutouts use the active bot account for Twitch's shoutout action. Automatic chat-message shoutouts use the public chat connection.",
                        "Polls, clips, markers, rewards, redemptions and Predictions use the selected channel's Twitch connection.",
                        "Rewards and Predictions require a Twitch Affiliate or Partner channel.",
                        "Use the ? button beside a page title for help without leaving the task you are doing.",
                    ],
                    Note =
                        "If a page asks you to reconnect, use its Reconnect to Twitch action and complete Twitch as the selected channel owner. Reconnecting the bot account does not repair a channel connection, and reconnecting the channel does not repair the bot account.",
                },
                new SiteGuideSection
                {
                    Heading = "When a result is uncertain",
                    Steps =
                    [
                        "Read the result shown on the page before repeating the action.",
                        "Reload the same page to check Twitch's current state and recent results.",
                        "Open Alerts if the page still needs attention.",
                        "Send the page name, selected channel, approximate time and alert text to the server owner. Never send Twitch tokens or secrets.",
                    ],
                },
            ],
            Next = [new SiteLink("Set up shoutouts", "twitch-operations/shoutouts")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/shoutouts",
            Eyebrow = "Native Twitch · Shoutouts",
            Title = "Send shoutouts and welcome raids",
            Summary =
                "Recommend another live channel now, or prepare one automatic welcome for each qualifying incoming raid.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-shoutouts.png",
                LightPhoneSource: "media/phone-light-native-shoutouts.png",
                DarkLaptopSource: "media/laptop-dark-native-shoutouts.png",
                LightLaptopSource: "media/laptop-light-native-shoutouts.png",
                PhoneAlt: "BlokeBot Shoutouts page on a phone showing a Twitch channel name field and the Send shoutout action.",
                LaptopAlt: "BlokeBot Shoutouts page showing the manual target and automatic raid shoutout settings.",
                "Manual shoutouts, automatic incoming-raid settings and recent outcomes stay on one task page."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Send a shoutout now",
                    Steps =
                    [
                        "Open Shoutouts, enter the other channel's Twitch name and select Send shoutout.",
                        "Wait for the result before trying again. The target must be live with viewers.",
                        "Use the displayed cooldown and Recent shoutouts to decide when another send is available.",
                    ],
                    Paragraphs =
                    [
                        "If BlokeBot asks for the bot account to be reconnected, restore that account's moderator role first, then reconnect it from Channel setup.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Welcome incoming raids automatically",
                    Steps =
                    [
                        "Automatic raid shoutouts are off by default. Open the section and turn them on when you are ready.",
                        "Set the minimum viewer count, then choose either a Native Twitch shoutout or a Chat message.",
                        "For a chat message, choose Regular, Pinned or Announcement. A pinned message can use a duration from 30 to 1,800 seconds or stay pinned until stream end; an announcement also needs a colour.",
                        "Write the message, check its preview and readiness note, then select Save automatic shoutouts.",
                    ],
                    Bullets =
                    [
                        "Message tokens include {twitch_handle}, {display_name}, {channel_url}, {viewer_count}, {last_game|fallback} and {stream_title|fallback}.",
                        "Last game and stream title need an inline fallback because Twitch may not provide them.",
                        "BlokeBot handles each eligible raid once. A failed native shoutout is not replaced with a chat message, and a failed announcement is not replaced with a regular message.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Check an automatic outcome",
                    Bullets =
                    [
                        "Automatic shoutout outcomes shows the newest raid results and why a send was skipped or incomplete.",
                        "Native shoutouts can be skipped while Twitch's cooldown is active.",
                        "A pinned message can be sent even when Twitch cannot pin it afterwards; the outcome states both parts.",
                        "Fix the connection or permission named by the outcome before the next raid. There is no retry or fallback action for an earlier raid.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a poll", "twitch-operations/polls")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/polls",
            Eyebrow = "Native Twitch · Polls",
            Title = "Ask viewers a question",
            Summary =
                "Save reusable poll questions, start one when you need it and watch the live vote totals.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-polls.png",
                LightPhoneSource: "media/phone-light-native-polls.png",
                DarkLaptopSource: "media/laptop-dark-native-polls.png",
                LightLaptopSource: "media/laptop-light-native-polls.png",
                PhoneAlt: "BlokeBot Polls page showing a saved question, current voting and poll controls.",
                LaptopAlt: "BlokeBot Polls page showing a saved question, current voting and poll controls.",
                "Saved questions, the active poll and recent results stay together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Save a poll question",
                    Steps =
                    [
                        "Open New poll template and enter the question and choices.",
                        "Set how long voting should stay open and whether viewers may spend Channel Points on extra votes.",
                        "Select Save template. The saved question appears in Run a poll.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run the poll",
                    Steps =
                    [
                        "Select Start poll beside the saved question you want to use.",
                        "Watch the choices and vote totals in the active poll.",
                        "Let Twitch finish it at the end of the duration, or select End poll to finish early.",
                    ],
                    Paragraphs =
                    [
                        "Twitch allows one active poll. A poll started elsewhere appears here after reload, so check its question before ending it.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "If the poll is unavailable",
                    Bullets =
                    [
                        "Use Reconnect to Twitch on this page and complete Twitch as the selected channel owner.",
                        "Finish the active poll before starting another.",
                        "Reload before repeating an action when the displayed totals or result may be stale.",
                    ],
                },
            ],
            Next = [new SiteLink("Save a clip or marker", "twitch-operations/clips-markers")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/clips-markers",
            Eyebrow = "Native Twitch · Clips & markers",
            Title = "Save a live moment",
            Summary =
                "Create a shareable clip now or leave a private marker to find in the stream recording later.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-clips-markers.png",
                LightPhoneSource: "media/phone-light-native-clips-markers.png",
                DarkLaptopSource: "media/laptop-dark-native-clips-markers.png",
                LightLaptopSource: "media/laptop-light-native-clips-markers.png",
                PhoneAlt: "BlokeBot Clips and markers page showing clip creation, stream marker and recent outcome controls.",
                LaptopAlt: "BlokeBot Clips and markers page showing clip creation, stream marker and recent outcome controls.",
                "Create a clip immediately or add a marker for the selected live channel."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create a clip",
                    Steps =
                    [
                        "Open Clips & markers while the selected channel is live.",
                        "Choose whether the clip should include the stream delay, then select Create clip once.",
                        "Open the completed clip from Clips and markers when Twitch finishes preparing it.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Place a stream marker",
                    Steps =
                    [
                        "Open Place a stream marker and add a short description.",
                        "Select Create marker. Find it later in the selected channel's stream recording.",
                    ],
                    Note =
                        "Markers need an active live stream with stream recordings enabled. Twitch may not allow them during reruns or premieres.",
                },
                new SiteGuideSection
                {
                    Heading = "Check an unfinished attempt",
                    Bullets =
                    [
                        "Use Check status or Check outcome when Twitch is still preparing or the first result was uncertain.",
                        "Do not make another clip or marker merely because Twitch is taking time; rechecking uses the recorded attempt.",
                        "Use Reconnect to Twitch if the page asks for the selected channel connection.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Manage rewards and redemptions", "twitch-operations/channel-points"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/channel-points",
            Eyebrow = "Native Twitch · Rewards & redemptions",
            Title = "Manage rewards and viewer requests",
            Summary =
                "Respond to waiting redemptions, manage BlokeBot rewards and create the next reward for your viewers.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-channel-points.png",
                LightPhoneSource: "media/phone-light-native-channel-points.png",
                DarkLaptopSource: "media/laptop-dark-native-channel-points.png",
                LightLaptopSource: "media/laptop-light-native-channel-points.png",
                PhoneAlt: "BlokeBot Rewards and redemptions page showing waiting requests, reward controls and age indicators.",
                LaptopAlt: "BlokeBot Rewards and redemptions page showing waiting requests, reward controls and age indicators.",
                "Waiting requests appear first, with visible age cues for requests that are becoming stale."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Answer waiting requests first",
                    Steps =
                    [
                        "Open Unfulfilled redemptions and read the reward, viewer input and waiting age.",
                        "Select Fulfil when the request is complete, or Cancel & refund when the viewer should receive their Channel Points back.",
                    ],
                    Bullets =
                    [
                        "Blue means the request is under 2 minutes old.",
                        "Amber means it has waited from 2 minutes to under 5 minutes.",
                        "Red means it has waited 5 minutes or longer and needs attention.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage or create a reward",
                    Bullets =
                    [
                        "Rewards created by BlokeBot can be edited, enabled, paused or deleted here.",
                        "Rewards created elsewhere are shown read-only so BlokeBot does not take ownership of them.",
                        "Create a reward appears after the waiting requests and current reward list. Set its cost and viewer instructions, then choose Create reward.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "When rewards are unavailable",
                    Bullets =
                    [
                        "Channel Points rewards require a Twitch Affiliate or Partner channel.",
                        "Use Reconnect to Twitch on this page when BlokeBot needs the selected channel's permission.",
                        "Reload before repeating a fulfil or refund when Twitch's result is unclear, then check Redemption history.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a Prediction", "twitch-operations/predictions")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/predictions",
            Eyebrow = "Native Twitch · Predictions",
            Title = "Run and settle a Prediction",
            Summary =
                "Save reusable Prediction questions, open Channel Points entries, then choose the winner or refund everyone.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-predictions.png",
                LightPhoneSource: "media/phone-light-native-predictions.png",
                DarkLaptopSource: "media/laptop-dark-native-predictions.png",
                LightLaptopSource: "media/laptop-light-native-predictions.png",
                PhoneAlt: "BlokeBot Predictions page showing a saved question, outcomes and controls for the active Prediction.",
                LaptopAlt: "BlokeBot Predictions page showing a saved question, outcomes and controls for the active Prediction.",
                "The active Prediction stays above reusable templates and recent settled results."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Save and start a Prediction",
                    Steps =
                    [
                        "Open New Prediction template and enter the question, possible outcomes and entry time.",
                        "Select Save template, then select Start Prediction beside the saved question.",
                        "Check the active question and outcome totals while viewers choose with Channel Points.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Close and settle it",
                    Steps =
                    [
                        "Select Lock when viewers should no longer enter.",
                        "After the real result is known, select Resolve as winner beside the correct outcome.",
                        "Select Cancel & refund only when the Prediction cannot be settled; Twitch returns the viewers' Channel Points.",
                    ],
                    Note =
                        "Resolution and refund cannot be undone. Confirm the selected channel, question and real result before choosing either action.",
                },
                new SiteGuideSection
                {
                    Heading = "If the Prediction needs attention",
                    Bullets =
                    [
                        "Predictions require a Twitch Affiliate or Partner channel.",
                        "A Prediction started elsewhere appears here after reload; inspect it before locking, refunding or resolving it.",
                        "Use Reconnect to Twitch if this page asks for the selected channel connection.",
                        "When Twitch's state is uncertain, wait a moment and reload before starting anything new.",
                    ],
                },
            ],
            Next = [new SiteLink("Return to Native Twitch help", "twitch-operations")],
        };

        yield return new SiteGuidePage
        {
            Route = "/commands",
            Eyebrow = "Custom commands",
            Title = "Create commands and scheduled messages",
            Summary =
                "Save reusable bot replies, connect them to chat words, keep counters and schedule reminders.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-custom-commands.png",
                LightPhoneSource: "media/phone-light-custom-commands.png",
                DarkLaptopSource: "media/laptop-dark-custom-commands.png",
                LightLaptopSource: "media/laptop-light-custom-commands.png",
                PhoneAlt: "Custom commands settings showing saved replies and a hydration reminder.",
                LaptopAlt: "Custom commands settings showing saved replies and a hydration reminder.",
                "Replies are reusable messages that commands and scheduled messages can send."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create a chat reply and command",
                    Steps =
                    [
                        "Open Custom commands, then Settings, and stay on the Commands tab.",
                        "Add a command, enter its command words without the exclamation mark and choose who may use it.",
                        "Open Message library, add a reply with at least one message, then return to Commands.",
                        "Choose the saved reply under What happens and select Save changes.",
                    ],
                    Paragraphs =
                    [
                        "Replies can include viewer, channel and argument placeholders. The Message library keeps reusable text separate from command structure.",
                    ],
                    Note =
                        "A command without a message cannot be saved. BlokeBot opens the relevant tab or section, focuses the field and shows the validation message instead of silently discarding the command.",
                },
                new SiteGuideSection
                {
                    Heading = "Add a counter, scheduled message or Twitch announcement",
                    Bullets =
                    [
                        "Counters let a command change and report a saved number.",
                        "Scheduled chat sends a saved reply on a timer, after chat activity or once a week.",
                        "Twitch announcement uses Twitch's coloured announcement surface. The bot must currently be a moderator and authorised for announcements.",
                        "If a scheduled send cannot happen, open its Alerts section and follow the displayed next action.",
                    ],
                },
            ],
            Next = [new SiteLink("Choose another tool", "tools")],
        };

        yield return new SiteGuidePage
        {
            Route = "/guessing",
            Eyebrow = "Guessing games",
            Title = "Set up and run a guessing game",
            Summary =
                "Create reusable round types and answers, collect one guess per viewer, then record the winning answer.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-guessing-workflow.webp",
                LightPhoneSource: "media/phone-light-guessing-workflow.webp",
                DarkLaptopSource: "media/laptop-dark-guessing-workflow.webp",
                LightLaptopSource: "media/laptop-light-guessing-workflow.webp",
                PhoneAlt: "Animated BlokeBot guessing dashboard moving through a live round workflow.",
                LaptopAlt: "Animated BlokeBot guessing dashboard moving through a live round workflow.",
                "The live dashboard keeps round status, votes, answers and winner controls together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a round type",
                    Steps =
                    [
                        "Turn on Guessing game and open its Settings page.",
                        "Create a round type, add every accepted answer, put comma-separated aliases after its canonical name, and choose any winner point reward.",
                        "Review the chat commands and bot replies, then save.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run the round",
                    Steps =
                    [
                        "Open the Guessing game Dashboard and choose the round type.",
                        "Start the round, let viewers guess, then stop guessing.",
                        "Enter the winning answer or one of its aliases and declare the winner.",
                    ],
                    Paragraphs =
                    [
                        "History and Leaderboard keep completed results. Public leaderboards can share rankings without dashboard access.",
                    ],
                },
            ],
            Next = [new SiteLink("Share a leaderboard", "leaderboards")],
        };

        yield return new SiteGuidePage
        {
            Route = "/points",
            Eyebrow = "Viewer points",
            Title = "Set up and manage points",
            Summary =
                "Give each viewer a channel balance that can be checked, transferred, adjusted, gambled or awarded as a prize.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-points-settings.png",
                LightPhoneSource: "media/phone-light-points-settings.png",
                DarkLaptopSource: "media/laptop-dark-points-settings.png",
                LightLaptopSource: "media/laptop-light-points-settings.png",
                PhoneAlt: "Points settings showing the point label, gambling chance, cooldown and chat command words.",
                LaptopAlt: "Points settings showing the point label, gambling chance, cooldown and chat command words.",
                "Points settings define the channel's terminology, gambling rules and command words."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Configure points",
                    Steps =
                    [
                        "Turn on Points and open Points Settings.",
                        "Choose the point label, gambling chance and wait between gambles.",
                        "Review command words and bot replies, then save.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage balances",
                    Bullets =
                    [
                        "Search for a viewer from the Points Dashboard.",
                        "Move points between viewers, add points or take points away after checking both names and the amount.",
                        "Use Recent changes to confirm adjustments and prizes. Delete balance only when the whole record should go.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a points giveaway", "giveaways")],
        };

        yield return new SiteGuidePage
        {
            Route = "/giveaways",
            Eyebrow = "Points giveaways",
            Title = "Run a giveaway",
            Summary =
                "Open timed entry while the channel is live, choose eligibility and winner count, then award random point prizes.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-points-settings.png",
                LightPhoneSource: "media/phone-light-points-settings.png",
                DarkLaptopSource: "media/laptop-dark-points-settings.png",
                LightLaptopSource: "media/laptop-light-points-settings.png",
                PhoneAlt: "Points settings page where channel point commands and giveaway rules are configured.",
                LaptopAlt: "Points settings page where channel point commands and giveaway rules are configured.",
                "Giveaway rules live on the Points settings page alongside the channel's points configuration."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Set the rules",
                    Steps =
                    [
                        "Open Points Settings and expand Giveaways.",
                        "Set entry time, prize range, winner count, eligibility and the wait between giveaways.",
                        "Save the settings before going live.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Start and finish",
                    Steps =
                    [
                        "While the Twitch channel is live, open the Points Dashboard and select Start in Giveaway.",
                        "Tell viewers to use the configured join command; each eligible viewer can enter once.",
                        "Select End to draw winners and award prizes, or Cancel to stop without winners.",
                    ],
                    Paragraphs =
                    [
                        "If Start is unavailable, check stream status, an existing giveaway and the cooldown shown by the dashboard.",
                    ],
                },
            ],
            Next = [new SiteLink("Review points", "points")],
        };

        yield return new SiteGuidePage
        {
            Route = "/leaderboards",
            Eyebrow = "Public results",
            Title = "Share a public leaderboard",
            Summary =
                "Viewers can open read-only guessing or points rankings without permission to manage the channel.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-guessing-leaderboard.png",
                LightPhoneSource: "media/phone-light-guessing-leaderboard.png",
                DarkLaptopSource: "media/laptop-dark-guessing-leaderboard.png",
                LightLaptopSource: "media/laptop-light-guessing-leaderboard.png",
                PhoneAlt: "Public guessing leaderboard showing players, correct guesses, rounds and accuracy.",
                LaptopAlt: "Public guessing leaderboard showing players, correct guesses, rounds and accuracy.",
                "Public leaderboards turn completed channel activity into a shareable read-only ranking."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Open and share it",
                    Steps =
                    [
                        "From Home or the sign-in page, choose Guessing or Points under Public leaderboard.",
                        "Enter the Twitch channel name and open the leaderboard.",
                        "Copy the browser address into Twitch panels, chat or community pages.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "If it is empty",
                    Bullets =
                    [
                        "Points rankings need viewer balances.",
                        "Guessing rankings need completed rounds.",
                        "The related tool must be on, and the channel must exist in BlokeBot.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a guessing game", "guessing")],
        };

        yield return new SiteGuidePage
        {
            Route = "/troubleshooting",
            Eyebrow = "Help and recovery",
            Title = "Understand a warning or offline bot",
            Summary =
                "Start with the message on the page. BlokeBot normally identifies the missing channel, permission, connection or tool.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Quick checks",
                    Steps =
                    [
                        "Confirm the selected channel.",
                        "Open Channel setup and check the tool switch and bot status.",
                        "Complete the Twitch action offered by the page.",
                        "Open Alerts and read the newest active alert.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Common Twitch failures",
                    Bullets =
                    [
                        "Wrong account: repeat the specific Channel or Bot connection action after signing out of Twitch in the pop-up.",
                        "Moderator-only action unavailable: confirm the bot is still a moderator, then reconnect if its grant predates the required scope.",
                        "Follower-only rejection: make the bot a moderator or manually follow the channel while signed in as the bot account.",
                        "Announcement rejected: confirm the bot is still a moderator, then reconnect the bot account using the action shown by Channel setup.",
                        "Dashboard script or stylesheet missing: ask the server owner to verify the reverse proxy path and static assets rather than repeatedly reconnecting Twitch.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Ask for useful help",
                    Paragraphs =
                    [
                        "If the problem remains, send the page name, selected channel, approximate time, alert text and any support reference to the server owner. Do not send Twitch secrets or tokens.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Check channel connections", "connect"),
                new SiteLink("Open the server owner guide", "server-owners"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/moderators",
            Eyebrow = "Moderator access",
            Title = "Let moderators help safely",
            Summary =
                "Channel owners decide whether current Twitch moderators can use BlokeBot and whether access is open to all moderators or limited by lists.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-channel-setup.png",
                LightPhoneSource: "media/phone-light-channel-setup.png",
                DarkLaptopSource: "media/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup containing readiness and access controls for the selected channel.",
                LaptopAlt: "Channel setup containing readiness and access controls for the selected channel.",
                "Moderator access is managed from Channel setup for the channel currently selected."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose an access mode",
                    Steps =
                    [
                        "Open Channel setup and expand Moderator help.",
                        "Turn on Let moderators help with this channel.",
                        "Choose All mods or Allowed list only, then maintain allowed and blocked names as needed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Know the boundary",
                    Bullets =
                    [
                        "Moderator access applies only to the selected channel.",
                        "An allowed current Twitch moderator can operate tools and change the selected channel's configuration.",
                        "BlokeBot rechecks Twitch moderator authority at sensitive changes and does not trust the role for the whole login session.",
                        "Turning moderator help off keeps the saved lists for later.",
                    ],
                    Note =
                        "If Twitch removes your moderator role, a later change can be refused even while the page is still open. Refresh the page or choose another channel; do not ask the server owner to bypass Twitch authority.",
                },
            ],
            Next = [new SiteLink("Manage channels", "channels")],
        };

        yield return new SiteGuidePage
        {
            Route = "/server-owners",
            Eyebrow = "Technical operations",
            Title = "Run a BlokeBot server",
            Summary =
                "Install the service, connect one Twitch application, provide trusted HTTPS and keep its private state backed up.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-admin.png",
                LightPhoneSource: "media/phone-light-admin.png",
                DarkLaptopSource: "media/laptop-dark-admin.png",
                LightLaptopSource: "media/laptop-light-admin.png",
                PhoneAlt: "The BlokeBot admin page, showing you which controls are available to server owners.",
                LaptopAlt: "The BlokeBot admin page, showing you which controls are available to server owners.",
                "The admin page allows you to configure the server to suit your needs, including allow lists for channels and manual channel setup."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "1. Install and run",
                    Paragraphs =
                    [
                        "Choose Nix, Docker or a source checkout, give BlokeBot a persistent data directory, and start the dashboard on a private address while you finish setup.",
                    ],
                    Links =
                    [
                        new SiteLink("Choose an installation route", "install"),
                        new SiteLink(
                            "Installation technical details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/Installation"
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "2. Create the Twitch application",
                    Paragraphs =
                    [
                        "Create one Website Integration application in the Twitch Developer Console. Register both public HTTPS callbacks, then provide its Client ID and Client Secret to BlokeBot without checking the secret into source.",
                    ],
                    Code =
                        "https://bot.example.com/auth/twitch/callback\nhttps://bot.example.com/oauth/callback",
                    Links =
                    [
                        new SiteLink(
                            "Open the Twitch Developer Console",
                            "https://dev.twitch.tv/console/apps"
                        ),
                        new SiteLink(
                            "Twitch application and callback details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/Twitch-Identity-and-OAuth"
                        ),
                    ],
                    Note =
                        "The callback text must match exactly, including scheme, host, port and path.",
                },
                new SiteGuideSection
                {
                    Heading = "3. Add HTTPS",
                    Paragraphs =
                    [
                        "Give the public dashboard a trusted HTTPS address. A typical deployment keeps BlokeBot on loopback behind Caddy, nginx or another reverse proxy that forwards the original scheme and host.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "HTTPS and reverse-proxy details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/HTTPS-and-Reverse-Proxy"
                        ),
                    ],
                    Note =
                        "Register the public HTTPS callbacks with Twitch, not the proxy's private HTTP address.",
                },
                new SiteGuideSection
                {
                    Heading = "4. Keep state private and backed up",
                    Paragraphs =
                    [
                        "BlokeBot keeps its SQLite database, OAuth token cache and automatically managed Data Protection keys in private persistent application state. Restrict that state to the service account and back it up from a stopped service or one consistent snapshot.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "State locations and backup details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/State-and-Secrets#state-and-backups"
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "5. Custom-bot credentials",
                    Paragraphs =
                    [
                        "Custom-bot encryption needs no operator configuration. ASP.NET Core manages Data Protection keys automatically in private persistent application state; Windows protects those keys with DPAPI LocalMachine.",
                        "A copied SQLite database or SQL backup does not expose reusable custom-bot tokens. Theft or compromise of the full state directory or the running host is outside that boundary.",
                        "When an upgrade finds old plaintext custom-bot credentials, it deletes them, disables that custom bot and alerts the channel owner to reconnect it.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "Custom-bot security details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/State-and-Secrets#custom-bot-credentials"
                        ),
                    ],
                },
            ],
            Next = [new SiteLink("Return to the user guide", "guide")],
        };
    }
}
