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
                        "Choose Trigger and enter its typed source values. For Custom command, select Input format: Raw chat message and enter the full Chat message, or use Structured inputs.",
                        "Open Clock and seed for repeatable values. Under Saved sample inputs, Generate sample inputs creates values from the current trigger schema.",
                        "Hypothetical action outcomes offers Would succeed or Failure (and Would start or Would already be running for Start countdown). Connected input overrides can supply a typed value to a connected input.",
                        "Select Run. The draft is validated before the isolated test starts.",
                        "Select a trace row to highlight its authored graph node. Nested calls include their call context, so repeated calls remain distinguishable.",
                        "Use Saved sample inputs to name or select inputs. Select Save sample to persist them for a saved flow; this does not save or enable the production graph or persist current result values.",
                    ],
                    Paragraphs =
                    [
                        "Scenarios simulate supported actions instead of sending chat, changing points, playing overlays or calling Twitch. Unsupported nodes and plugin execution are rejected, not run live. Outcome choices apply to action nodes in the authored flow, not individual actions inside nested calls.",
                        "Known missing permissions and disabled features fail the test. Current live eligibility and Twitch state are not checked: the selected outcomes are hypothetical, not permission to act live.",
                        "Current typed values, would-send messages and virtual times belong only to the latest test. Editing inputs or the draft, rerunning, leaving or closing the test, and reloading clear or invalidate them. Sensitive values and values with unknown provenance stay masked; displays are bounded.",
                        "Save sample explicitly stores sample inputs. Configuration Transfer includes only selected generated samples that still match their portable recipe. Other authored samples, current values and traces are excluded.",
                        "Traces opens recent test results. The ordered details show successes, failures and selected call context. Expired or truncated details are reported rather than reconstructed. Recent live runs remain available in the run drawer.",
                        "Validate and repair the flow before enabling it. The editor asks for confirmation when enabling public-message, points, overlay or Twitch effects. Duplicate creates a disabled graph copy without run history.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run a saved flow for real",
                    Steps =
                    [
                        "Add Manual run from the Toolbox. Configure its explicit Data value in the existing inspector and connect its Flow output to the desired actions.",
                        "Save, validate and enable the flow. Resolve required channel tools and permissions first.",
                        "Choose Run saved flow. This is a real authorized run for the selected channel, using the saved enabled graph and its configured data, not your unsaved draft.",
                    ],
                    Paragraphs =
                    [
                        "Run saved flow is unavailable while the draft is changed, unsaved, busy, disabled or has no Manual run source. Multiple Manual run nodes each start their own matching source-node run for the same invocation.",
                        "Real runs may send public chat, change points, play cues or use Twitch actions. Test draft and its Run button remain an isolated simulation. A real flow's Cancel countdown action is ordinary cancellation, not quiet restart cancellation.",
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
                        "Ordinary supported input Expression uses the execution context: arguments, supported actor/channel fields and variables, subject to the existing privacy gates. Named command ports are not global expression variables.",
                        "Transform input Expression has a restricted safe trigger view: arguments only, not ordinary actor/channel context. Connect an Actor input for public viewer fields.",
                        "Transform output expressions see only declared inputs and their allowed fields, not raw event context, private IDs, roles, services or other outputs of the same Transform. format_number is Transform-only.",
                        "For legacy raw tokens, decimal(arguments[0]) converts numeric Text. If joining a tail, first declare an Arguments input named args, then use join(args, 1, size(args), \" \"). The end index is exclusive and clamped. This joins tokens; it does not preserve the original whitespace like typed Remaining text.",
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
                    Heading = "Timer recipe: command inputs",
                    Paragraphs =
                    [
                        "Build !timer <seconds> <name> with seven nodes: Custom command, one CEL Transform, Condition, three Send chat message nodes (Started, Finished and Input error), and Delay. This uses a durable flow Delay, not a named countdown.",
                    ],
                    Steps =
                    [
                        "Create a custom command named timer with alias !timer. Under What happens choose Run automation flows. In Arguments, enable named arguments.",
                        "Add seconds first: Number, Required, Minimum (exclusive) 0, Maximum 922337203685.477. The inclusive maximum is the native Delay limit in milliseconds divided by 1000.",
                        "Add name last: Remaining text, Required, Maximum words 10. Remaining text preserves internal and trailing whitespace; quotes are literal, not grouping characters. The command tail also has the existing 500-character and 50-word limits.",
                        "Save the command. In the flow's Custom command source, select timer (configuration field custom-command-id). Its typed outputs are argument.seconds (Number), argument.name (Text), and actor (Viewer).",
                    ],
                    Links = [new SiteLink("Create and configure the command", "commands")],
                },
                new SiteGuideSection
                {
                    Heading = "Timer recipe: format the values",
                    Steps =
                    [
                        "In CEL Transform, create three Inputs from Connected sources. Keep each Required: CEL name seconds / Number from argument.seconds; name / Text from argument.name; viewer / Actor from actor. Connect these Data outputs; do not enter the names as ordinary expression variables.",
                        "Add four Outputs. Set Output name, Type, Required? to Required, and CEL expression as shown below. Transform.valid and the other Transform names in this recipe mean these output labels; the editor assigns their port IDs. Each expression reads declared inputs, not another output.",
                    ],
                    Code = """
                        valid (Boolean)
                        seconds * 1000 == decimal(int(seconds * 1000))

                        milliseconds (Number)
                        seconds * 1000

                        started (Text)
                        ${viewer.display_name} has started a ${seconds} seconds ${name} timer

                        finished (Text)
                        ${viewer.display_name}'s ${name} timer has finished!
                        """,
                    Note =
                        "The Number schema bounds make the integer conversion safe. valid rejects fractional milliseconds without truncating them. int here receives a Number, not Text. Send chat Fixed text does not interpolate: build both messages in this Transform instead.",
                },
                new SiteGuideSection
                {
                    Heading = "Timer recipe: connect Flow and Data",
                    Steps =
                    [
                        "Set Condition Predicate to Connected. Connect Transform.valid to its predicate Data input.",
                        "Set Started and Finished Message to Connected. Connect Transform.started to Started.message and Transform.finished to Finished.message.",
                        "Set Delay Duration to Connected. Connect Transform.milliseconds to its duration Data input (binding field duration-value-milliseconds). The value is numeric milliseconds, not seconds or formatted text.",
                        "Leave Input error Message Fixed: Use a positive number of seconds that gives whole milliseconds, then a name of one to ten words.",
                        "Connect the Flow ports below. Every destination Flow input is flow. Keep failure policy Stop. The Transform is pure and has no incoming Flow connection.",
                        "Validate and repair the draft before saving and enabling it. Both Custom commands and Automations must be on for real command starts.",
                    ],
                    Code = """
                        Custom command.flow → Condition.flow
                        Condition.yes → Started.flow
                        Condition.no → Input error.flow
                        Started.complete → Delay.flow
                        Delay.complete → Finished.flow
                        """,
                    Paragraphs =
                    [
                        "Flow schedules actions; Data only supplies values. The parser rejects missing, malformed, nonpositive or above-maximum seconds and missing or over-ten-word names before a real run is admitted. Real chat receives the parser's error/usage; an offline run reports Invalid. A positive fractional millisecond takes Condition.no and sends only Input error, never Started.",
                        "Missing or incompatible Data and failed expression evaluation stop before Started; they are not promised the friendly Input error message. These are parsing and flow checks, not a guarantee against later send or Delay failure. The native Delay also checks actual DateTime headroom when reached; an extreme duration or later failure can occur after Started. There is no rollback or guaranteed external delivery.",
                        "An admitted Delay keeps frozen inputs and can resume its continuation after restart. Named countdowns instead cancel quietly on restart and do not replay their old warning, finish or cancellation events.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Timer recipe: test without sending chat",
                    Steps =
                    [
                        "Open Test draft, choose the Custom command Trigger, and select Input format: Raw chat message. Enter !timer 1.001 tea break in Chat message.",
                        "Open Other source fields and set Viewer display name to Viewer. Leave Connected input overrides off and the chat actions at Would succeed under Hypothetical action outcomes. Select Run.",
                        "Inspect the Yes branch and typed milliseconds value 1001. The virtual timeline shows Started at 0 ms and Finished at 1001 ms, with the two messages below. No live chat is sent and the test does not wait 1.001 wall-clock seconds.",
                        "Try a ten-word name, then eleven words, a missing value or bad number. Parser errors are Invalid. Try !timer 0.0001 tea break: only the No branch and Input error message run.",
                    ],
                    Code = """
                        Viewer has started a 1.001 seconds tea break timer
                        Viewer's tea break timer has finished!
                        """,
                    Note =
                        "These current values are ephemeral and privacy-gated. Save sample stores inputs only, explicitly; it is not an assertion suite or a saved result. See Test and enable safely for unsupported actions, hypothetical outcomes and export limits.",
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
                        "For chat messages, connect event data to CEL Transform and connect its Text output to Send chat message.",
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
