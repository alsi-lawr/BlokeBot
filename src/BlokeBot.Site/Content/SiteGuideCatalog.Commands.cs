namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommandPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/commands",
            Eyebrow = "Custom commands",
            Title = "Create commands and scheduled messages",
            Summary =
                "Save reusable bot replies, connect them to chat words, keep counters and schedule reminders.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/commands/phone-dark-custom-commands.png",
                LightPhoneSource: "media/commands/phone-light-custom-commands.png",
                DarkLaptopSource: "media/commands/laptop-dark-custom-commands.png",
                LightLaptopSource: "media/commands/laptop-light-custom-commands.png",
                PhoneAlt: "BlokeBot Custom commands on a phone with the saved command list and the selected command's Basics step.",
                LaptopAlt: "BlokeBot Custom commands with the saved command list beside the selected command's name, command words and chat preview.",
                "The saved command list sits beside the selected command. Its words and the viewer reply stay visible together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create a chat reply and command",
                    Steps =
                    [
                        "Open Custom commands, then Settings, and stay on the Commands tab.",
                        "Add a command and enter its command words without the exclamation mark. Choose who can use it.",
                        "Open Message library, add a reply with at least one message, then return to Commands.",
                        "Choose the saved reply under What happens and select Save changes.",
                    ],
                    Paragraphs =
                    [
                        "Replies can include viewer, channel and argument placeholders. The Message library keeps reusable text separate from command structure.",
                    ],
                    Note =
                        "BlokeBot cannot save a command without a message. BlokeBot opens the relevant tab or section. It focuses the field and shows the validation message. It keeps the command.",
                },
                new SiteGuideSection
                {
                    Heading = "Add random values to saved replies",
                    Bullets =
                    [
                        "{random_from|one|two} picks one value.",
                        "{random_between|1|10} picks an inclusive whole number.",
                        "Each random token occurrence makes a fresh pick.",
                        "{random_viewer} picks from Twitch chatters currently connected to chat. The active bot account must be a moderator with connected-chatter access.",
                        "If Twitch cannot return the complete chatter list, {random_viewer} becomes empty text.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Add a counter, scheduled message or Twitch announcement",
                    Bullets =
                    [
                        "Counters let a command change and report a saved number.",
                        "Scheduled chat sends a saved reply on a timer, after chat activity or once a week.",
                        "Twitch announcement uses Twitch's coloured announcement surface. The bot must currently be a moderator and authorized for announcements.",
                        "If a scheduled send cannot happen, open its Alerts section and follow the displayed next action.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Start visual automation flows",
                    Paragraphs =
                    [
                        "Choose Run automation flows under What happens. Every enabled flow whose Custom command event selects this command starts from the same chat invocation.",
                    ],
                    Bullets =
                    [
                        "Custom commands and Automations must both be on for a command to start a flow.",
                        "Build the connection in Visual automations. The command does not keep a second flow picker.",
                        "Turning either feature off keeps the command, flow, and run history but suppresses new work without replaying it later.",
                    ],
                    Links = [new SiteLink("Build a visual automation", "automations")],
                },
            ],
            Next =
            [
                new SiteLink("Publish the available viewer commands", "commands/catalog"),
                new SiteLink("Choose another tool", "tools"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/commands/catalog",
            Eyebrow = "Chat commands · Viewer discovery",
            Title = "Publish the commands viewers can use now",
            Summary =
                "Choose one global Commands trigger. Viewers can discover a safe list of main command names for the selected channel's current state.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/commands/phone-dark-viewer-command-catalog.png",
                LightPhoneSource: "media/commands/phone-light-viewer-command-catalog.png",
                DarkLaptopSource: "media/commands/laptop-dark-viewer-command-catalog.png",
                LightLaptopSource: "media/commands/laptop-light-viewer-command-catalog.png",
                PhoneAlt: "Channel setup on a phone that shows the global Commands trigger and expanded Available viewer commands list.",
                LaptopAlt: "Channel setup that shows the global Commands trigger, expanded Available viewer commands list and a command-name conflict.",
                "Channel setup shows the same viewer-safe list of main command names that the global chat trigger publishes."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose the global trigger",
                    Steps =
                    [
                        "Choose the channel, open Channel setup and expand Commands.",
                        "Enter the command words that viewers can use. Separate them with commas and omit the exclamation mark. The default is commands.",
                        "Select Save Commands. The setting applies to the whole selected channel, not to one Custom Command.",
                        "Leave the field blank and save only when you intend to disable the viewer command catalog.",
                    ],
                    Paragraphs =
                    [
                        "If a word is already owned by another command, Channel setup names the conflict. Choose another word and save. BlokeBot does not silently replace the existing command.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Check what viewers will see",
                    Steps =
                    [
                        "Open Available viewer commands inside the Commands section. It starts collapsed so the setup page stays compact.",
                        "Review the current main command names and any conflict or availability explanation.",
                        "In chat, send the saved trigger such as !commands to publish the same ordered list.",
                    ],
                    Bullets =
                    [
                        "The disclosure requests a fresh snapshot whenever it opens. Supported state changes also refresh an open list. They do not replace an unsaved trigger draft.",
                        "The list includes its own saved trigger and only commands an ordinary viewer can use.",
                        "Moderator-only commands and private administration actions are never disclosed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Understand main names",
                    Paragraphs =
                    [
                        "Each Custom Command contributes only the first command word in its saved alias list. That main-name rule keeps the catalog short and predictable. Secondary aliases still work in chat but are not advertised.",
                    ],
                    Bullets =
                    [
                        "Built-in commands use their supported public main names.",
                        "The catalog omits a moderator-only Custom Command even when its main name works for moderators.",
                        "If two routes claim the same word, the catalog reports the shadowed entry. It does not report both as available.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Why commands appear or disappear",
                    Bullets =
                    [
                        "Guess and round-summary commands appear only while the guessing game has the applicable active round state.",
                        "Giveaway entry appears only while a giveaway accepts entries.",
                        "Request-board and play-queue commands follow the channel's saved, enabled boards and queues.",
                        "Moment and clip commands depend on live-stream identity. They disappear when the channel is offline or Twitch stream identity is unavailable.",
                        "Feature commands disappear when that feature is off for the selected channel.",
                    ],
                    Paragraphs =
                    [
                        "If BlokeBot identifies the cause, it explains the unavailable feature beside the list. If no viewer commands are available, the disclosure reports this fact. It does not publish an incorrect list.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Long lists and live changes",
                    Paragraphs =
                    [
                        "BlokeBot keeps the command order stable. If the chat response exceeds the Twitch limit, BlokeBot splits the list across ordinary replies. It does not omit or duplicate names.",
                        "A game, giveaway, board, queue, feature switch or stream-liveness change can alter membership. Before you prepare an announcement or stream instructions, reopen Available viewer commands for a new check.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Fix common catalog problems",
                    Bullets =
                    [
                        "The chat trigger does nothing: confirm that at least one Commands word is saved. Resolve each conflict in Channel setup.",
                        "A Custom Command alias is absent: only its first saved word is advertised.",
                        "A moderator command is absent: the public catalog deliberately shows viewer-safe commands only.",
                        "A game or Moment command is absent: check the feature and active round or giveaway. Check the named live-stream state.",
                        "The list is empty: enable or configure a viewer feature, board, queue or Custom Command. Reopen the disclosure.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Create Custom Commands", "commands"),
                new SiteLink("Choose another channel tool", "tools"),
            ],
        };
    }
}
