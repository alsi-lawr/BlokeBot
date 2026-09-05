namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityInteractionPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/request-boards",
            Eyebrow = "Community interaction · Requests",
            Title = "Run a structured request board",
            Summary = "Manage viewer requests.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-request-boards.png",
                LightPhoneSource: "media/community/phone-light-request-boards.png",
                DarkLaptopSource: "media/community/laptop-dark-request-boards.png",
                LightLaptopSource: "media/community/laptop-light-request-boards.png",
                PhoneAlt: "The Sample Channel public request board on a narrow screen with open rules and the submission form.",
                LaptopAlt: "The Sample Channel Request boards moderator page with a saved Game night requests board and its configuration.",
                "Moderators configure the board at /requests. Viewers use its public channel-and-board address."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose the right view",
                    Bullets =
                    [
                        "Collect consistent viewer suggestions.",
                        "Moderate their lifecycle.",
                        "Keep point charges and public status clear.",
                        "A channel owner or permitted moderator chooses the channel. That person opens Request boards at /requests to manage boards.",
                        "Open public board copies the viewer route /requests/{channel}/{board-name}. Anyone can read a saved board.",
                        "A viewer signs in with Twitch to submit.",
                        "A viewer signs in with Twitch to vote.",
                        "A viewer signs in with Twitch to withdraw.",
                        "Chat participants can discover boards with !requests.",
                        "Website and chat actions use the same board.",
                        "Website and chat actions use the same limits.",
                        "Website and chat actions use the same votes.",
                        "Website and chat actions use the same request states.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the board's Command and URL name. Do not type the braces.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The stable public queue order accounts for moderator priority.",
                        "The stable public queue order accounts for votes.",
                        "The stable public queue order accounts for assigned queue position.",
                        "Field type: Text.",
                        "Field type: Link.",
                        "Field type: Choose from a list.",
                        "Field type: Number.",
                        "Field type: Twitch clip link.",
                    ],
                    Heading = "Configure a board",
                    Steps =
                    [
                        "Select New.",
                        "Enter a Command and URL name.",
                        "Enter a title and description.",
                        "Select whether the board accepts submissions.",
                        "Set the point cost.",
                        "Set the refund policy.",
                        "Set the active-submission limit.",
                        "Set the submission cooldown.",
                        "Set the voting switch.",
                        "Set the per-viewer vote limit.",
                        "Add only the fields that participants need.",
                        "Set its label.",
                        "Set the applicable length limits.",
                        "Set the applicable choice limits.",
                        "Set the applicable number limits.",
                        "Select Save board.",
                        "Use Open public board.",
                        "Read the Board rules as a participant.",
                    ],
                    Paragraphs = ["A field uses one of the types below."],
                },
                new SiteGuideSection
                {
                    Heading = "Submit and vote",
                    Bullets =
                    [
                        "On the public page, sign in with Twitch.",
                        "Complete Title and the configured fields.",
                        "Select Submit request.",
                        "The page shows the request number and its current public state.",
                        "In chat, use !request <board> <title> | field=value | category=value | tags=a,b. Required field keys come from that board's configuration.",
                        "Use !requestvote <request-number> to vote in chat, or Vote on the public board. A repeated vote does not add another vote.",
                        "A submitter can Withdraw an active request from the public page. The public page never shows private moderator text.",
                    ],
                    Note =
                        "BlokeBot recognizes a repeated delivery of the same chat submission. It reports the original request and does not create or charge another.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Approved requests can move to In queue or Accepted.",
                        "In queue or Accepted requests can move to Completed.",
                        "Submitters can select Withdraw.",
                        "BlokeBot gives merged duplicates the Merged into another request state.",
                        "Chat action: !requestapprove followed by one request number.",
                        "Chat action: !requestreject followed by one request number.",
                        "Chat action: !requestqueue followed by one request number.",
                        "Chat action: !requestaccept followed by one request number.",
                        "Chat action: !requestcomplete followed by one request number.",
                    ],
                    Heading = "Moderate the lifecycle",
                    Steps =
                    [
                        "Review the submitted values and any possible-duplicate warning.",
                        "If participants need them, set the public category and tags.",
                        "If participants need them, set the priority and Public note.",
                        "Move Awaiting review to Approved or Rejected.",
                        "Use Merge with the target request number when two entries are the same request.",
                        "When the dashboard is not convenient, use one applicable chat action.",
                        "To merge in chat, use !requestmerge <source-number> <target-number>.",
                    ],
                    Paragraphs =
                    [
                        "Private moderator note and Private rejection reason remain moderator-only. Put public participant context in Public note instead.",
                        "The public board keeps the Merged into another request outcome and the target request's combined support.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Points and recovery",
                    LegacyAnchor = "points-failure-and-recovery",
                    Bullets =
                    [
                        "When the board accepts the initial submission, it holds the cost from the viewer's available balance before moderator review.",
                        "The reservation starts as No points charged.",
                        "The reservation then moves to Points held.",
                        "The reservation finishes as Points refunded or Points charged.",
                        "Never charge the viewer manually as well.",
                        "Completion charges the held points.",
                        "A closure follows the selected policy:",
                        "Closure policy: Never refund.",
                        "Closure policy: Refund if rejected or withdrawn.",
                        "Closure policy: Refund if not fulfilled.",
                        "If the submission fails, correct the cause that the message identifies.",
                        "A validation error can reject a submission.",
                        "A cooldown can reject a submission.",
                        "A limit can reject a submission.",
                        "The balance can reject a submission.",
                        "Submit once. If an outcome is already visible, reload before you try again.",
                        "If request state and points still disagree after reload, leave the request unchanged.",
                        "Include the channel in the information for the server owner.",
                        "Include the board name in the information for the server owner.",
                        "Include the request number in the information for the server owner.",
                        "Include the approximate time in the information for the server owner.",
                        "Include the visible message in the information for the server owner.",
                        "Do not share Twitch tokens or private notes.",
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
            Summary = "Manage viewer participation.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-play-with-viewers.png",
                LightPhoneSource: "media/community/phone-light-play-with-viewers.png",
                DarkLaptopSource: "media/community/laptop-dark-play-with-viewers.png",
                LightLaptopSource: "media/community/laptop-light-play-with-viewers.png",
                PhoneAlt: "The Sample Channel Community night party viewer page with its public queue rule and optional entry form.",
                LaptopAlt: "The Sample Channel Play with viewers moderator page shows a saved queue and party size. It also shows fair-selection configuration.",
                "The moderator route /queues and viewer route /queues/{channel}/{queue-name} share one live queue."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose identities and permissions",
                    Bullets =
                    [
                        "Open a queue and collect optional public entry details.",
                        "Run ready checks.",
                        "Deliver private lobby information only to participants.",
                        "A channel owner or permitted moderator chooses the channel and opens Play with viewers at /queues.",
                        "Open viewer page uses /queues/{channel}/{queue-name}. A viewer must sign in with Twitch to join. There is no unsigned typed-login fallback.",
                        "The public page never shows moderator controls.",
                        "The public page never shows priorities.",
                        "The public page never shows moderator notes.",
                        "The public page never shows lobby messages.",
                        "Configured entry fields and their answers are public.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the queue's Command and URL name. Do not type the braces.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "An optional public entry field can describe platform.",
                        "An optional public entry field can describe region.",
                        "An optional public entry field can describe rank.",
                        "An optional public entry field can describe preferred role.",
                    ],
                    Heading = "Configure and open the queue",
                    Steps =
                    [
                        "Select New.",
                        "Set the Command and URL name.",
                        "Set the Queue name.",
                        "Set the Game or activity.",
                        "Set the Party size.",
                        "Select First to join or Viewers who played least recently.",
                        "Set Ready expiry.",
                        "Set History retention.",
                        "Set Skip/no-show exclusion.",
                        "Add optional public entry fields.",
                        "Add any required roles in role=count form.",
                        "Select whether the public page can show participant names.",
                        "Turn Queue open on.",
                        "Save the queue.",
                        "Use Open viewer page.",
                        "Inspect the page at two widths.",
                    ],
                    Paragraphs =
                    [
                        "Every configured field is optional and public on the viewer page and Viewer Queue overlay. Lobby messages and moderator notes remain private.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Join from the page or chat",
                    Bullets =
                    [
                        "On the viewer page, complete the requested fields.",
                        "Select Join.",
                        "Check position reports the current place.",
                        "Leave removes the entry.",
                        "I'm ready answers an active ready check.",
                        "In chat, use !queue [queue].",
                        "To join in chat, use !join [queue] key=value.",
                        "To leave in chat, use !leave [queue].",
                        "To check position in chat, use !position [queue].",
                        "To answer a ready check in chat, use !ready [queue].",
                        "The queue name is optional when the channel has only one queue.",
                        "A second join request keeps one entry. The signed-in Twitch identity is authoritative and blocks a duplicate entry from a second typed identity.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Unavailable participant action: Replace one.",
                        "Unavailable participant action: Skip.",
                        "Unavailable participant action: No-show.",
                        "A queue entry can have the state Waiting.",
                        "A queue entry can have the state Awaiting response.",
                        "A queue entry can have the state Ready.",
                        "A queue entry can have the state Selected.",
                        "A queue entry can have the state Left queue.",
                        "A queue entry can have the state Skipped.",
                        "A queue entry can have the state Did not respond.",
                    ],
                    Heading = "Select and run a party",
                    Steps =
                    [
                        "Review Waiting viewers and the visible next-candidate order.",
                        "Adjust Priority or Moderator note only when a documented channel rule requires it.",
                        "Start a Ready check for candidates.",
                        "Then select Select next party.",
                        "Use Keep party to retain the current group.",
                        "When someone cannot play, use the applicable action.",
                        "Enter the Lobby message.",
                        "Select Whisper party.",
                        "Before you start, check that the whisper succeeded.",
                        "Never paste a private lobby code into public chat as a fallback.",
                    ],
                    Paragraphs =
                    [
                        "Moderators can use !queueopen [queue] and !queueclose [queue]. Close the queue before you resolve a disputed selection. New joins cannot move the visible candidate order.",
                        "Participants must use I'm ready or !ready before Ready expiry.",
                        "The configured exclusion prevents immediate re-entry after a skip or no-show.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover safely",
                    Bullets =
                    [
                        "If a participant misses Ready expiry, run a new ready check or use Replace one. Use No-show only when the channel's exclusion rule must apply.",
                        "If a whisper fails, check that the bot connection can whisper.",
                        "Retry Whisper party only after the page reports the failure.",
                        "Do not reveal the private message publicly.",
                        "If selection cannot satisfy required roles, leave the current party intact.",
                        "Adjust the pool or role requirements.",
                        "Select again.",
                        "History retention removes old participation data after the configured period. If you shorten it, future fairness evidence changes. Record that channel decision before you save.",
                    ],
                },
            ],
            Next = [new SiteLink("Capture and recap community moments", "community/moments")],
        };
    }
}
