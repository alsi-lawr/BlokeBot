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
                        "Nest existing tokens in either random function: {random_between|1|{arg1}} or {random_from|Hello {user}|{random_between|1|10}}.",
                        "Command arguments are {arg1} through {arg9}; {args} is their joined text. Counter commands also provide {count}.",
                        "Number bounds must be ordered whole numbers from -2147483648 to 2147483647. Negative and equal bounds work.",
                        "Missing, non-whole-number, out-of-range or reversed resolved bounds send a short explanation instead of the affected reply. Other command actions keep their normal behavior.",
                        "Braces and pipes in argument or chatter text stay text, not extra tokens or choices.",
                        "Each random token occurrence makes a fresh pick.",
                        "{random_viewer} picks from Twitch chatters currently connected to chat. The active bot account must be a moderator with connected-chatter access.",
                        "If Twitch cannot return the complete chatter list, {random_viewer} becomes empty text.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Declare and edit saved values",
                    Steps =
                    [
                        "Open Custom commands. Select Variables.",
                        "Select New variable or open Dictionaries and select New dictionary.",
                        "Enter a name. Select User for each viewer or Global for this channel.",
                        "For a variable, select Number or Text. Enter its default. For a dictionary, enter the missing entry value.",
                        "Select Save changes.",
                        "Select the definition. For User scope, enter a viewer login. Select Select viewer. Check the displayed viewer ID.",
                        "Change the value. Select Save changes.",
                        "For a dictionary, select Edit beside an entry. To add a key, select Add entry. Enter the key. Select Open entry.",
                        "Select the entry type and change its value. Select Save changes.",
                    ],
                    Bullets =
                    [
                        "User values belong to a stable Twitch ID in one channel. A changed login does not change the owner. Global values are shared only within the selected channel.",
                        "A variable uses its default until you save a value. Get does not create a dictionary entry. An absent entry returns the missing entry value.",
                        "Set keeps an existing entry's type. Set creates an absent entry as Text. Increment creates an absent Number entry from zero.",
                        "Number values are signed 64-bit whole numbers. Text stays literal. Names and keys have no feature-specific cap. Stored text and record counts have no feature-specific cap. Database storage remains finite. Chat-send limits are separate.",
                        "Saved values have no automatic expiry. Reset restores one variable value to its default. Delete entry removes one dictionary key. Other viewers and keys do not change.",
                        "Save definition changes a name or default, not live values. Scope and type are fixed. Delete definition removes its saved data across viewers. Commands that reference a deleted or renamed definition fail until you change their tokens.",
                        "A stale edit does not overwrite a newer command update. Your edit stays on screen. Reload the current value before you save again.",
                        "Configuration packages contain definitions, defaults and the command's single-argument setting. They do not contain live values, dictionary entries or invocation results. Fresh imports use defaults and empty dictionaries. Omission does not reset existing destination data.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Use saved values in replies",
                    Steps =
                    [
                        "Open Message library. Select a reply.",
                        "Open Insert a saved-value token. Select a definition and operation.",
                        "For a dictionary, supply a key or a nested token such as {arg1}.",
                        "For Set, supply text or a token. For Increment, supply a whole-number amount or a token.",
                        "Select Insert token. Open Sandbox preview. Check the result without a live save.",
                        "Save the reply. Select it for a command.",
                    ],
                    Bullets =
                    [
                        "{var_get|user|hugs} returns a viewer's saved number. {var_inc|user|hugs|1} adds one and returns the new total.",
                        "{var_set|user|bio|{args}} saves text and returns it. {var_get|global|hugs_total} returns a channel-wide value.",
                        "{dict_get|user|profile|{arg1}} reads the key from the first argument. {dict_set|user|profile|{arg1}|{arg2}} saves an entry. {dict_inc|user|scores|{arg1}|{arg2}} adds the second argument to a Number entry.",
                        "Nested tokens resolve inside before outside. Selected occurrences resolve once, from left to right. Reads after writes see the new value. Unselected variants and random choices do not write.",
                        "Arguments, saved text and chatter text remain literal. Braces and pipes in those values do not become more tokens.",
                        "With Whole remaining text is one argument on, !bio uses its one-argument reply for a nonempty phrase. {arg1} and {args} contain the exact literal phrase. !bio without text uses its zero-argument reply. Other commands keep word parsing.",
                        "Changes require a message ID. User-scoped values require a stable viewer ID. Missing references and keys, incorrect Number input or overflow produce an explanation without private values. A failed transaction does not save partial changes.",
                        "Access, feature, cooldown and invocation limits apply before new effects. A repeated committed message ID does not repeat effects.",
                        "If chat delivery fails after commit, values and the computed reply remain. A retry of the same message ID reuses that reply without another increment or random choice. This does not guarantee exactly-once chat delivery or add a retry schedule.",
                        "Sandbox previews use a copy of saved values. They do not send chat or save changes. They do not claim invocations or advance real variants.",
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
