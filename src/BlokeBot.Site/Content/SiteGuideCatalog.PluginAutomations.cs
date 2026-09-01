namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginAutomationPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development/automations",
            Eyebrow = "Plugin automations",
            Title = "Automation nodes",
            Summary =
                "Plugins can add typed source, action, value, control, and transform nodes to the standard automation editor.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Node types",
                    Facts =
                    [
                        new("source", "Starts a flow and returns declared output values."),
                        new("action", "Uses declared input values to perform an allowed effect."),
                        new("value", "Returns a value from its configuration and input values."),
                        new(
                            "control",
                            "Uses declared input values to control the progress of a flow."
                        ),
                        new(
                            "transform",
                            "Converts declared input values into declared output values."
                        ),
                    ],
                    Paragraphs =
                    [
                        "Each definition belongs to one plugin feature. BlokeBot rejects work from an old plugin worker or disabled feature.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Ports",
                    Paragraphs =
                    [
                        "Each input and output declares a value kind and whether the value is required. The generator creates the corresponding Lua handler type.",
                    ],
                    Facts =
                    [
                        new("string", "A string value."),
                        new("number", "A finite number value."),
                        new("boolean", "A true or false value."),
                        new("array", "An ordered list of plugin values."),
                        new("map", "A string-keyed map of plugin values."),
                    ],
                    Note =
                        "Nil represents an absent value. An automation port cannot use Nil as its value kind.",
                },
                new SiteGuideSection
                {
                    Heading = "Definitions",
                    Code = """
                        [[automationDefinitions]]
                        id = "store-submission"
                        featureId = "collection"
                        kind = "action"
                        name = "Store link submission"
                        description = "Stores one submitted link."
                        module = "queue"
                        entryPoint = "store_submission"
                        outputs = []

                        [[automationDefinitions.inputs]]
                        id = "url"
                        name = "URL"
                        valueKind = "string"
                        required = true
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Templates",
                    Paragraphs =
                    [
                        "A template defines nodes, configuration, and typed data edges. Each feature lists the templates that it owns.",
                        "BlokeBot validates the graph before feature enablement. Invalid ports, duplicate writers, missing inputs, and cycles prevent enablement.",
                    ],
                    Code = """
                        [[automationTemplates]]
                        id = "queue-submission"
                        featureId = "collection"
                        name = "Queue submitted links"

                        [[automationTemplates.nodes]]
                        id = "store"
                        definitionId = "store-submission"
                        configuration = { kind = "map", properties = [] }
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Generated flows",
                    Bullets =
                    [
                        "Feature enablement creates a valid flow and records which plugin and template created it in one transaction.",
                        "Repeated recovery does not create a duplicate flow.",
                        "BlokeBot does not overwrite or rename a conflicting host flow.",
                        "If a user deletes the generated flow, a later disable and enable can create it again.",
                        "A plugin update replaces the current node definitions and handlers.",
                        "Plugin removal deletes plugin definitions, dependent flows, nodes, run history, ledgers, and source receipts.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Updates and disabled features",
                    Paragraphs =
                    [
                        "BlokeBot checks the plugin, host, feature, node definition, and active installation before each automation call.",
                        "An old worker result cannot start or complete a run. Feature disablement and plugin removal cancel affected calls and runs.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Testing and releases", "plugin-development/test-publish"),
                new SiteLink("Automation editor", "automations"),
            ],
        };
    }
}
