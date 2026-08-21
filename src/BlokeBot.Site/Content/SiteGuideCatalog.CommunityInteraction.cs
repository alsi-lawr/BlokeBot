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
            Summary =
                "Collect consistent viewer suggestions, moderate their lifecycle and keep point charges and public status understandable.",
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
                        "A channel owner or permitted moderator chooses the channel. That person opens Request boards at /requests to manage boards.",
                        "Open public board copies the viewer route /requests/{channel}/{board-name}. Anyone can read a saved board. A viewer signs in with Twitch to submit, vote or withdraw.",
                        "Chat participants can discover boards with !requests. Website and chat actions use the same board, limits, votes and request states.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the board's Command and URL name. Do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Configure a board",
                    Steps =
                    [
                        "Select New, give the board a Command and URL name, title and description, then choose whether it accepts submissions.",
                        "Set the point cost, refund policy, active-submission limit, submission cooldown, voting switch and per-viewer vote limit.",
                        "Add only the fields participants need. A field can be Text, Link, Choose from a list, Number or Twitch clip link. Set its label and applicable length, choice or number limits.",
                        "Select Save board, then use Open public board and read the Board rules exactly as a participant will see them.",
                    ],
                    Paragraphs =
                    [
                        "The public queue order explains the complete stable order. Moderator priority, votes and assigned queue position refine that order. They do not hide a different participant rule.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Submit and vote",
                    Bullets =
                    [
                        "On the public page, sign in with Twitch, complete Title and the configured fields, then select Submit request. The page shows the request number and its current public state.",
                        "In chat, use !request <board> <title> | field=value | category=value | tags=a,b. Required field keys come from that board's configuration.",
                        "Use !requestvote <request-number> to vote in chat, or Vote on the public board. A repeated vote does not add another vote.",
                        "A submitter can Withdraw an active request from the public page. Private moderator text is never shown there.",
                    ],
                    Note =
                        "BlokeBot recognizes a repeated delivery of the same chat submission. It reports the original request and does not create or charge another.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate the lifecycle",
                    Steps =
                    [
                        "Review the submitted values and any possible-duplicate warning. Set public category, tags, priority and Public note when they help participants.",
                        "Move Awaiting review to Approved or Rejected. Approved requests can move to In queue or Accepted. In queue or Accepted requests can move to Completed. Submitters can select Withdraw. BlokeBot gives merged duplicates the Merged into another request state.",
                        "Use Merge with the target request number when two entries are the same request. The public board keeps the Merged into another request outcome and the target request's combined support.",
                        "When the dashboard is not convenient, use !requestapprove, !requestreject, !requestqueue, !requestaccept or !requestcomplete followed by one request number.",
                        "To merge in chat, use !requestmerge <source-number> <target-number>.",
                    ],
                    Paragraphs =
                    [
                        "Private moderator note and Private rejection reason remain moderator-only. Put public participant context in Public note instead.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Points, failure and recovery",
                    Bullets =
                    [
                        "When the board accepts the initial submission, it holds the cost from the viewer's available balance before moderator review. The reservation moves from No points charged to Points held, then finishes as Points refunded or Points charged. Never charge the viewer manually as well.",
                        "Completion charges the held points. A closure follows the selected policy: Never refund, Refund if rejected or withdrawn, or Refund if not fulfilled.",
                        "If validation, the cooldown, a limit or the balance rejects a submission, correct the message shown and submit once. If an outcome is already visible, reload before you try again.",
                        "If request state and points still disagree after reload, leave the request unchanged. Send the channel, board name, request number, approximate time and visible message to the server owner. Do not share Twitch tokens or private notes.",
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
                "Open a queue and collect optional public entry details. Run ready checks. Deliver private lobby information only to participants.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-play-with-viewers.png",
                LightPhoneSource: "media/community/phone-light-play-with-viewers.png",
                DarkLaptopSource: "media/community/laptop-dark-play-with-viewers.png",
                LightLaptopSource: "media/community/laptop-light-play-with-viewers.png",
                PhoneAlt: "The Sample Channel Community night party viewer page with its public queue rule and optional entry form.",
                LaptopAlt: "The Sample Channel Play with viewers moderator page with a saved queue, party size and fair-selection configuration.",
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
                        "Open viewer page uses /queues/{channel}/{queue-name}. A viewer must sign in with Twitch to join. There is no unsigned typed-login fallback.",
                        "Moderator controls, priorities, moderator notes and lobby messages are never shown on the public page. Configured entry fields and their answers are public.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the queue's Command and URL name. Do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Configure and open the queue",
                    Steps =
                    [
                        "Select New, set the Command and URL name, Queue name, Game or activity and Party size.",
                        "Choose First to join or Viewers who played least recently. The viewer page states the applicable fair-selection rule before anyone joins.",
                        "Set Ready expiry, History retention and Skip/no-show exclusion. Add optional public entry fields and any required roles in role=count form.",
                        "Choose whether the public page can show participant names. Turn Queue open on and save. Inspect Open viewer page at two widths.",
                    ],
                    Paragraphs =
                    [
                        "Every configured field is optional and public on the viewer page and Viewer Queue overlay. Examples include platform, region, rank and preferred role. Lobby messages and moderator notes remain private.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Join from the page or chat",
                    Bullets =
                    [
                        "On the viewer page, fill the requested fields and select Join. Check position reports the current place. Leave removes the entry. I'm ready answers an active ready check.",
                        "In chat use !queue [queue], !join [queue] key=value, !leave [queue], !position [queue] and !ready [queue]. The queue name is optional when the channel has only one queue.",
                        "A second join request keeps one entry. The signed-in Twitch identity is authoritative and blocks a duplicate entry from a second typed identity.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Select and run a party",
                    Steps =
                    [
                        "Review Waiting viewers and the visible next-candidate order. Entries move through Waiting, Awaiting response, Ready, Selected, Left queue, Skipped and Did not respond. Adjust Priority or Moderator note only when a documented channel rule requires it.",
                        "Start a Ready check for candidates. Participants must use I'm ready or !ready before Ready expiry. Then select Select next party.",
                        "Use Keep party to retain the current group. Use Replace one, Skip or No-show when someone cannot play. The configured exclusion prevents immediate re-entry after a skip or no-show.",
                        "Enter the Lobby message and select Whisper party. Confirm success before you start. Never paste a private lobby code into public chat as a fallback.",
                    ],
                    Paragraphs =
                    [
                        "Moderators can use !queueopen [queue] and !queueclose [queue]. Close the queue before you resolve a disputed selection. New joins cannot move the visible candidate order.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover safely",
                    Bullets =
                    [
                        "If a participant misses Ready expiry, run a new ready check or use Replace one. Use No-show only when the channel's exclusion rule must apply.",
                        "If a whisper fails, verify that the bot connection can whisper. Retry Whisper party only after the page reports the failure. Do not reveal the private message publicly.",
                        "If selection cannot satisfy required roles, leave the current party intact. Adjust the pool or role requirements and select again.",
                        "History retention removes old participation data after the configured period. If you shorten it, future fairness evidence changes. Record that channel decision before you save.",
                    ],
                },
            ],
            Next = [new SiteLink("Capture and recap community moments", "community/moments")],
        };
    }
}
