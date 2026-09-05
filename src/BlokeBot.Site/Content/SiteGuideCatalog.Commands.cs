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
            Summary = "Configure Custom commands.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/commands/phone-dark-custom-commands.png",
                LightPhoneSource: "media/commands/phone-light-custom-commands.png",
                DarkLaptopSource: "media/commands/laptop-dark-custom-commands.png",
                LightLaptopSource: "media/commands/laptop-light-custom-commands.png",
                PhoneAlt: "BlokeBot Custom commands on a phone with the saved command list and the selected command's Basics step.",
                LaptopAlt: "BlokeBot Custom commands shows the saved command list beside the selected command. That command's name and command words are visible. Its chat preview is visible.",
                "The saved command list sits beside the selected command. Its words and the viewer reply stay visible together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "For a command without a message, BlokeBot opens the relevant tab or section.",
                        "It focuses the field.",
                        "It shows the validation message.",
                        "It keeps the command.",
                        "Save reusable bot replies.",
                        "Connect them to chat words.",
                        "You can also keep counters.",
                        "You can also schedule reminders.",
                        "Replies can include viewer placeholders.",
                        "Replies can include channel placeholders.",
                        "Replies can include argument placeholders.",
                    ],
                    Heading = "Create a chat reply and command",
                    Steps =
                    [
                        "Open Custom commands.",
                        "Open Settings.",
                        "Stay on the Commands tab.",
                        "Add a command.",
                        "Enter its command words without the exclamation mark.",
                        "Select who can use it.",
                        "Open Message library.",
                        "Add a reply with at least one message.",
                        "Return to Commands.",
                        "Select the saved reply under What happens.",
                        "Select Save changes.",
                    ],
                    Paragraphs =
                    [
                        "The Message library keeps reusable text separate from command structure.",
                    ],
                    Note = "BlokeBot cannot save a command without a message.",
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
                    Heading = "Other chat tools",
                    LegacyAnchor = "add-a-counter-scheduled-message-or-twitch-announcement",
                    Bullets =
                    [
                        "Counters let a command change and report a saved number.",
                        "Scheduled chat can send a saved reply on a timer.",
                        "Scheduled chat can send a saved reply after chat activity.",
                        "Scheduled chat can send a saved reply once a week.",
                        "Twitch announcement uses Twitch's colored announcement surface. The bot must currently be a moderator and authorized for announcements.",
                        "If a scheduled send cannot happen, open its Alerts section. Follow the displayed next action.",
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
                        "If either feature is off, BlokeBot keeps the command.",
                        "If either feature is off, BlokeBot keeps the flow.",
                        "If either feature is off, BlokeBot keeps the run history.",
                        "It suppresses new work and does not replay it later.",
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
                LaptopAlt: "Channel setup shows the global Commands trigger. It also shows the expanded Available viewer commands list and a command-name conflict.",
                "Channel setup shows the same viewer-safe list of main command names that the global chat trigger publishes."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "If another command owns a word, Channel setup names the conflict.",
                        "Select another word.",
                        "Save the change.",
                    ],
                    Heading = "Choose the global trigger",
                    Steps =
                    [
                        "Select the channel.",
                        "Open Channel setup.",
                        "Expand Commands.",
                        "Enter the command words that viewers can use.",
                        "Separate the words with commas.",
                        "Omit the exclamation mark.",
                        "Select Save Commands.",
                        "To disable the viewer command catalog, leave the field blank.",
                        "Save the blank field only if you intend to disable the catalog.",
                    ],
                    Paragraphs =
                    [
                        "BlokeBot does not replace the existing command without your choice.",
                        "The default Commands trigger is commands.",
                        "The Commands trigger applies to the whole selected channel, not to one Custom Command.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Check what viewers will see",
                    Paragraphs =
                    [
                        "Available viewer commands starts collapsed to keep the setup page compact.",
                    ],
                    Steps =
                    [
                        "Open Available viewer commands inside the Commands section.",
                        "Review the current main command names and any conflict or availability explanation.",
                        "In chat, send the saved trigger such as !commands to publish the same ordered list.",
                    ],
                    Bullets =
                    [
                        "The disclosure requests a fresh snapshot whenever it opens. Supported state changes also refresh an open list. They do not replace an unsaved trigger draft.",
                        "The list includes its own saved trigger and only commands an ordinary viewer can use.",
                        "The catalog never discloses moderator-only commands or private administration actions.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Main command names",
                    LegacyAnchor = "understand-main-names",
                    Paragraphs =
                    [
                        "Each Custom Command contributes only the first command word in its saved alias list. This rule keeps the catalog short and predictable. Secondary aliases still work in chat but do not appear in the catalog.",
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
                    Heading = "Command availability",
                    LegacyAnchor = "why-commands-appear-or-disappear",
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
                    Bullets =
                    [
                        "Queue changes can also alter catalog membership.",
                        "Feature-switch changes can also alter catalog membership.",
                        "Stream-liveness changes can also alter catalog membership.",
                        "Changes to games can alter catalog membership.",
                        "Changes to giveaways can alter catalog membership.",
                        "Changes to boards can alter catalog membership.",
                    ],
                    Heading = "Long lists and live changes",
                    Paragraphs =
                    [
                        "BlokeBot keeps the command order stable. If the chat response exceeds the Twitch limit, BlokeBot splits the list across ordinary replies. It does not omit or duplicate names.",
                        "Before you prepare an announcement or stream instructions, reopen Available viewer commands for a new check.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Fix common catalog problems",
                    Bullets =
                    [
                        "If the chat trigger does nothing, check that at least one Commands word is saved. Resolve each conflict in Channel setup.",
                        "If a Custom Command alias is absent, check its position. The catalog shows only the first saved word.",
                        "A moderator command is absent: the public catalog deliberately shows viewer-safe commands only.",
                        "If a game or Moment command is absent, check the feature and active round or giveaway. Check the named live-stream state.",
                        "If the list is empty, enable or configure a source of viewer commands.",
                        "A viewer feature can supply commands.",
                        "A board can supply commands.",
                        "A queue can supply commands.",
                        "A Custom Command can supply commands.",
                        "Reopen the disclosure.",
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
