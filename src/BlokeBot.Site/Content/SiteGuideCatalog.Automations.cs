namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateAutomationPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/automations",
            Eyebrow = "Automations",
            Title = "Connect channel events to automatic actions",
            Summary =
                "Build channel flows on a snapped grid. Connect events, data, conditions, and actions. Then validate and test the flow before you enable it.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/automations/phone-dark-grid-visual-automations.png",
                LightPhoneSource: "media/automations/phone-light-grid-visual-automations.png",
                DarkLaptopSource: "media/automations/wide-dark-grid-visual-automations.png",
                LightLaptopSource: "media/automations/wide-light-grid-visual-automations.png",
                PhoneAlt: "The Visual automations editor on a phone. It shows compact nodes, connections, and the validation state.",
                LaptopAlt: "The Visual automations editor. It shows the Toolbox, typed nodes, connections, and the node inspector.",
                "Use Grid view to arrange nodes. Use List view to inspect the same nodes and connections."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn Automations on",
                    Steps =
                    [
                        "Choose the channel in the top bar and open Channel setup.",
                        "Open Chat tools and turn on Automations. BlokeBot saves the change at once.",
                        "Open Automations. Create a flow and choose a trigger from the Toolbox.",
                    ],
                    Paragraphs =
                    [
                        "Automations is off by default for each channel. The channel owner or a permitted moderator manages it for the selected channel.",
                    ],
                    Note =
                        "If Automations is off, saved flows and run history remain. Events do not start flows.",
                },
                new SiteGuideSection
                {
                    Heading = "Build on the snapped grid",
                    Steps =
                    [
                        "Search the Toolbox. Add one or more triggers, then add values, transforms, controls, and actions.",
                        "Select a node to open its inspector on the right. Drag an output port to a compatible input port or node.",
                        "Click a node to open its details on the card. Click the same node again to close the details.",
                        "Drag nodes on the 24-pixel grid, or move them with the keyboard. Use the canvas controls to set the flow direction and connection style.",
                        "Use Ctrl and the mouse wheel to zoom. Drag the background to move the canvas. Hold Alt and drag to select nodes.",
                        "Save the draft. Validate it, and fix each disconnected node, invalid input, cycle, missing reference, or unavailable channel tool.",
                    ],
                    Note =
                        "Each trigger starts a separate flow run. If triggers connect to the same node, each run continues through that node. A flow cannot contain a cycle.",
                },
                new SiteGuideSection
                {
                    Heading = "Test and enable safely",
                    Bullets =
                    [
                        "Test flow runs a sample event through the graph and reports each node result. It does not send chat, change points, play an overlay, or call Twitch.",
                        "You cannot enable an invalid graph. If a flow can send public messages, change points, play overlays, or call Twitch, the editor shows a warning first.",
                        "The run drawer shows the latest sample and recent live results. It names the node that failed, even if the flow continued.",
                        "Duplicate copies the graph and node positions as a disabled draft. It does not copy run history.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Understand a flow",
                    Paragraphs =
                    [
                        "A flow uses Flow connections to schedule nodes. Data connections supply values and do not define run order.",
                    ],
                    Bullets =
                    [
                        "A selected custom command, Twitch event or Channel Points redemption can start a flow.",
                        "A Condition uses one Boolean input. Yes continues when the value is true. No continues when the value is false.",
                        "A Delay waits the configured time before the flow continues. Delayed flows do not block chat or other automations.",
                        "Actions send chat messages, play overlay cues, complete Channel Points redemptions and run native Twitch operations.",
                    ],
                    Note =
                        "Grid and List edit the same saved graph. List shows connections, input choices, sources, types, and repair states.",
                },
                new SiteGuideSection
                {
                    Heading = "Write CEL expressions",
                    Paragraphs =
                    [
                        "CEL is a small language that calculates a value from node inputs.",
                        "This BlokeBot guide explains the inputs and safety limits for automations. The official CEL reference explains the general language.",
                    ],
                    Bullets =
                    [
                        "Declare each Transform input under Inputs before you use its CEL name in an output expression.",
                        "Set Required? to Required or Optional for each declared input.",
                        "Edit the CEL name to rename a declared input. BlokeBot rewrites that name in the expressions of the same node.",
                        "Connect an Actor input to use actor.display_name or actor.login. Other Actor fields are not available.",
                        "Bind arguments to a declared Arguments input. Output CEL can then use that declared input name.",
                        "An output can return Text, Number, Boolean, Timestamp, or a null value with one of these types.",
                        "Use format_number(number) to format a Number. Add a second value from 0 through 6 for exact decimal places.",
                        "If a port changes, BlokeBot keeps the connection and marks it for repair. Repair it or disconnect it.",
                        "CEL cannot use raw event context, private data, IDs, roles, services, or outputs from the same Transform.",
                    ],
                    Code =
                        "${actor.display_name} rolled ${format_number(number)}\nnumber >= 75\narguments_input.size() > 0",
                    Links =
                    [
                        new SiteLink(
                            "Official introduction to CEL",
                            "https://github.com/cel-expr/cel-spec/blob/master/doc/intro.md"
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Use event data safely",
                    Bullets =
                    [
                        "Each source publishes typed values for the flow. Values can include the viewer, command text, channel, event time and live stream identity.",
                        "Chat messages, marker descriptions, poll and prediction questions and expressions can include automation variables that carry those values.",
                        "BlokeBot treats viewer identities and typed text as sensitive. By default, it keeps these values out of overlays and logs.",
                    ],
                    Paragraphs =
                    [
                        "The privacy notice covers automation run records and the source event context.",
                    ],
                    Links = [new SiteLink("Read the privacy notice", "privacy")],
                },
                new SiteGuideSection
                {
                    Heading = "Know what happens on failure",
                    Bullets =
                    [
                        "Every step has a failure choice: stop the flow or continue past the failure. A stopped flow records the step that failed. Later steps do not run.",
                        "BlokeBot never repeats an action because its outcome was uncertain. It does not send a chat message, clip, or Twitch operation twice to force an answer.",
                        "Twitch can deliver the same event more than once. BlokeBot keeps a short-lived receipt, so a repeated delivery inside ten minutes starts nothing extra.",
                        "Actions inherit their feature switches. Overlay cues need Overlays. Native Twitch operations need their Native Twitch feature. Command starts need Custom commands.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Start flows from Twitch events", "automations/events"),
                new SiteLink("Choose what automations do", "automations/actions"),
            ],
        };
    }
}
