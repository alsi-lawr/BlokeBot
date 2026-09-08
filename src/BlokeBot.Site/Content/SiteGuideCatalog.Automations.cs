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
            Summary = "Build, test and reuse visual channel automations.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/automations/phone-dark-grid-visual-automations.png",
                LightPhoneSource: "media/automations/phone-light-grid-visual-automations.png",
                DarkLaptopSource: "media/automations/wide-dark-grid-visual-automations.png",
                LightLaptopSource: "media/automations/wide-light-grid-visual-automations.png",
                PhoneAlt: "A draft test on a phone, with its graph and ordered node trace.",
                LaptopAlt: "The automation editor with scenario source fields and a trace from the unsaved draft.",
                "Test draft opens scenarios beside the graph. A completed scenario produces a selectable node trace."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Build channel flows on a snapped grid.",
                        "Connect a flow.",
                        "Validate the flow.",
                        "Before you enable the flow, test it.",
                        "An automation connects events.",
                        "An automation connects data.",
                        "An automation connects conditions.",
                        "An automation connects actions.",
                    ],
                    Heading = "Turn Automations on",
                    Steps =
                    [
                        "Select the channel in the top bar.",
                        "Open Channel setup.",
                        "Open Chat tools.",
                        "Turn on Automations.",
                        "Open Automations.",
                        "Create a flow.",
                        "Select a trigger from the Toolbox.",
                    ],
                    Paragraphs =
                    [
                        "Automations is off by default for each channel. The channel owner or a permitted moderator manages it for the selected channel.",
                        "BlokeBot saves the change at once.",
                    ],
                    Note =
                        "If Automations is off, saved flows and run history remain. Events do not start flows.",
                },
                new SiteGuideSection
                {
                    Heading = "Build on the snapped grid",
                    Steps =
                    [
                        "Search the Toolbox.",
                        "Add one or more triggers.",
                        "Add values.",
                        "Add transforms.",
                        "Add controls.",
                        "Add actions.",
                        "Select a node to open its inspector on the right.",
                        "Drag an output port to a compatible input port or node.",
                        "Click a node to open its details on the card.",
                        "Click the same node again to close the details.",
                        "Drag nodes on the 24-pixel grid, or move them with the keyboard.",
                        "Use the canvas controls to set the flow direction and connection style.",
                        "Use Ctrl and the mouse wheel to zoom.",
                        "Drag the background to move the canvas.",
                        "Hold Alt and drag to select nodes.",
                        "Save the draft.",
                        "Validate it.",
                        "Repair each disconnected node.",
                        "Correct each invalid input.",
                        "Remove each cycle.",
                        "Repair each missing reference.",
                        "Resolve each unavailable channel tool.",
                    ],
                    Note =
                        "Each trigger starts a separate flow run. If triggers connect to the same node, each run continues through that node. A flow cannot contain a cycle.",
                },
                new SiteGuideSection
                {
                    Heading = "Test and enable safely",
                    Steps =
                    [
                        "Open a flow and select Test draft. You do not need to save your current graph edits.",
                        "Choose a saved scenario or select New. Choose the trigger and edit its typed Source fields.",
                        "Set Clock and seed for repeatable values. Generate fixture creates source values from the current trigger schema.",
                        "Choose Success or Failure under Action outcomes. Connected input overrides can supply a specific typed value to a connected input.",
                        "Select Run scenario. The draft is validated before the isolated test starts.",
                        "Select a trace row to highlight its authored graph node. Nested calls include their call context, so repeated calls remain distinguishable.",
                        "Select Save scenario to keep the fixture for a saved flow. This does not save or enable the production graph.",
                    ],
                    Paragraphs =
                    [
                        "Scenarios simulate supported actions instead of sending chat, changing points, playing overlays or calling Twitch. Unsupported nodes and plugin execution are rejected, not run live. Outcome choices apply to action nodes in the authored flow, not individual actions inside nested calls.",
                        "Configuration Transfer includes only selected generated fixtures that still match their portable recipe. Other authored fixtures and traces are excluded.",
                        "Traces opens recent test results. The ordered details show successes, failures and selected call context. Expired or truncated details are reported rather than reconstructed. Recent live runs remain available in the run drawer.",
                        "Validate and repair the flow before enabling it. The editor asks for confirmation when enabling public-message, points, overlay or Twitch effects. Duplicate creates a disabled graph copy without run history.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Reuse a subflow",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/automations/phone-dark-list-visual-automations.png",
                        LightPhoneSource: "media/automations/phone-light-list-visual-automations.png",
                        DarkLaptopSource: "media/automations/wide-dark-list-visual-automations.png",
                        LightLaptopSource: "media/automations/wide-light-list-visual-automations.png",
                        PhoneAlt: "A subflow's Exit inspector on a phone, with Outputs declarations and separate Return values.",
                        LaptopAlt: "The Subflows library and a subflow in List view, with its Exit output contract and connected return value.",
                        "Entry declares Inputs. Exit declares Outputs and supplies their Return values. Grid and List edit the same subflow."
                    ),
                    Steps =
                    [
                        "Use Flows and Subflows at the top of the left library to browse either collection. Switching library tabs does not replace your open draft.",
                        "Select the top-level New subflow action. New flow creates an ordinary event-driven flow instead.",
                        "In the selected Entry inspector, add Inputs and choose each port's name, type, Nullable and Sensitive settings. These values become Entry's Data outputs inside the subflow.",
                        "Add the internal nodes and connect the Flow route from Entry to Exit.",
                        "Select Exit. Declare Outputs, then set each Return value to Fixed, Connected or Expression. A declaration defines the contract; its return binding supplies the actual value.",
                        "Select Review subflow. Edit Description there and review validation and any incompatible callers before Publish subflow. Draft or description changes require a fresh review.",
                        "In another flow or subflow, add Call subflow from the Toolbox. Select its target in that node's inspector, then configure the typed input values or connections.",
                    ],
                    Paragraphs =
                    [
                        "The library exposes only the current subflow. Compatible published changes apply automatically to saved callers' future runs. Repeated calls in one operation use the same resolved current definition. Already admitted runs keep their frozen graph and do not switch partway through.",
                        "An incompatible caller is flagged and cannot start with the old interface as a fallback. Open its Call subflow node, use Update inputs and outputs when offered, and repair invalid values or connections. The editor retains incompatible authored work until you explicitly repair or remove it.",
                        "Selecting a library row or either New action can replace the open document and uses the unsaved-change prompt. Browsing tabs alone does not discard it.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Extract selected nodes",
                    Steps =
                    [
                        "Select a supported connected group of nodes and choose Extract selection.",
                        "Review the generated typed Inputs and Outputs, boundary connections, name conflicts and proposed Call subflow replacement.",
                        "Correct unsupported cuts or invalid connections before publishing the extracted subflow.",
                        "Select Create subflow and replace. This publishes the extracted subflow and replaces the selection in the open draft with one undoable edit; it does not save the production flow.",
                    ],
                    Note =
                        "Undo restores the draft selection and connections. It does not undo the separate publication of the library subflow. Arbitrary branch boundaries are not supported; the preview reports cuts that cannot preserve routing or failure order.",
                },
                new SiteGuideSection
                {
                    Heading = "Flow connections",
                    LegacyAnchor = "understand-a-flow",
                    Paragraphs =
                    [
                        "A flow uses Flow connections to schedule nodes. Data connections supply values and do not define run order.",
                    ],
                    Bullets =
                    [
                        "List shows connections.",
                        "List shows input choices.",
                        "List shows sources.",
                        "List shows types.",
                        "List shows repair states.",
                        "A selected custom command can start a flow.",
                        "A selected Twitch event can start a flow.",
                        "A selected Channel Points redemption can start a flow.",
                        "A Condition uses one Boolean input. Yes continues when the value is true. No continues when the value is false.",
                        "A Delay waits the configured time before the flow continues. Delayed flows do not block chat or other automations.",
                        "Actions can send chat messages.",
                        "Actions can play overlay cues.",
                        "Actions can complete Channel Points redemptions.",
                        "Actions can run native Twitch operations.",
                    ],
                    Note = "Grid and List edit the same saved graph.",
                },
                new SiteGuideSection
                {
                    Heading = "Write CEL expressions",
                    Paragraphs =
                    [
                        "CEL is a small language that calculates a value from node inputs.",
                        "This guide describes the inputs and safety limits for BlokeBot automations. The official CEL reference describes the general language.",
                    ],
                    Bullets =
                    [
                        "Declare each Transform input under Inputs before you use its CEL name in an output expression.",
                        "Set Required? to Required or Optional for each declared input.",
                        "Edit the CEL name to rename a declared input. BlokeBot rewrites that name in the expressions of the same node.",
                        "Connect an Actor input to use actor.display_name or actor.login. Other Actor fields are not available.",
                        "Bind arguments to a declared Arguments input. Output CEL can then use that declared input name.",
                        "An output can return Text or a null value with the Text type.",
                        "An output can return Number or a null value with the Number type.",
                        "An output can return Boolean or a null value with the Boolean type.",
                        "An output can return Timestamp or a null value with the Timestamp type.",
                        "Use format_number(number) to format a Number. Add a second value from 0 through 6 for exact decimal places.",
                        "If a port changes, BlokeBot keeps the connection and marks it for repair. Repair it or disconnect it.",
                        "CEL cannot use raw event context and private data.",
                        "CEL cannot use IDs and roles.",
                        "CEL cannot use services and outputs from the same Transform.",
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
                    Heading = "Event data and privacy",
                    LegacyAnchor = "use-event-data-safely",
                    Bullets =
                    [
                        "Each source publishes typed values for the flow.",
                        "Source values can include the viewer and command text.",
                        "Source values can include the channel and event time.",
                        "Source values can include live stream identity.",
                        "Chat messages can include automation variables that carry those values.",
                        "Marker descriptions can include automation variables that carry those values.",
                        "Poll questions can include automation variables that carry those values.",
                        "Prediction questions can include automation variables that carry those values.",
                        "Expressions can include automation variables that carry those values.",
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
                    Heading = "Flow failures",
                    LegacyAnchor = "know-what-happens-on-failure",
                    Bullets =
                    [
                        "Every step has a failure choice: stop the flow or continue past the failure. A stopped flow records the step that failed. Later steps do not run.",
                        "BlokeBot never repeats an action because its outcome is uncertain.",
                        "It does not send a chat message twice to force an answer.",
                        "It does not send a clip twice to force an answer.",
                        "It does not send a Twitch operation twice to force an answer.",
                        "Twitch can deliver the same event more than once. BlokeBot keeps a short-lived receipt, so a repeated delivery inside ten minutes starts nothing extra.",
                        "Actions inherit their feature switches.",
                        "Overlay cues need Overlays.",
                        "Native Twitch operations need their Native Twitch feature.",
                        "Command starts need Custom commands.",
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
