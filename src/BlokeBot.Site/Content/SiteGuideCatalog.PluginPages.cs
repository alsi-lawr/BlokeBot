namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginPagePages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development/pages",
            Eyebrow = "Plugin pages",
            Title = "Plugin pages",
            Summary =
                "Generated pages use BlokeBot page documents for standard controls. Embedded pages provide a contained browser experience.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Page types",
                    Facts =
                    [
                        new(
                            "Generated pages",
                            "A Lua renderer returns a versioned page document. BlokeBot renders standard text, status, form, table, and list sections."
                        ),
                        new(
                            "Embedded pages",
                            "BlokeBot serves declared browser assets in one contained frame. The browser bridge validates messages."
                        ),
                    ],
                    Note =
                        "Do not treat an embedded frame as a security sandbox for a trusted plugin. The frame contains product layout and styles.",
                },
                new SiteGuideSection
                {
                    Heading = "Generated page declarations",
                    Code = """
                        [[generatedPages]]
                        id = "queue-management"
                        featureId = "collection"
                        route = "queue-management"
                        title = "Queue management"
                        module = "pages"
                        renderEntryPoint = "render_queue"
                        """,
                    Paragraphs =
                    [
                        "The renderer receives version, hostId, and sessionId. It returns document version 1 with an ordered list of sections.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Page documents",
                    Code = """
                        render_queue = function(input)
                          return {
                            version = 1,
                            introduction = "Review one item.",
                            sections = {
                              {
                                kind = "status",
                                title = "Queue ready",
                                description = "The queue can accept reviews.",
                                tone = "neutral",
                              },
                            },
                          }
                        end
                        """,
                    Bullets =
                    [
                        "text contains static body content.",
                        "status contains a short state with a supported tone.",
                        "form uses one declared page action.",
                        "table contains rows with declared columns.",
                        "list contains short item collections.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Forms and actions",
                    Paragraphs =
                    [
                        "Each page action input is declared in plugin.toml. The form must use the same field IDs, required flags, and value kinds.",
                        "BlokeBot rejects an unknown field, a missing required field, a duplicate field, or a value with the wrong kind.",
                    ],
                    Facts =
                    [
                        new("text", "This type produces a string value."),
                        new("multiline", "This type produces a string value."),
                        new("choice", "This type produces one declared string value."),
                        new("number", "This type produces a finite number value."),
                        new("boolean", "This type produces a true or false value."),
                    ],
                    Note =
                        "The generator creates one exact LuaLS input class for each page action.",
                },
                new SiteGuideSection
                {
                    Heading = "Embedded page declarations",
                    Code = """
                        [[embeddedPages]]
                        id = "queue-bridge"
                        featureId = "collection"
                        route = "queue-bridge"
                        title = "Queue bridge"
                        documentAsset = "queue-document"
                        assets = ["queue-document", "queue-script", "queue-styles"]
                        messageOrigins = []
                        """,
                    Paragraphs =
                    [
                        "Every served asset must belong to the page declaration. Each successful asset response uses the page content policy and nosniff headers.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Browser bridge",
                    Paragraphs =
                    [
                        "The bridge checks the frame source, HTTPS origin, session, protocol, schema, message size, selected host, and active plugin state.",
                        "A message ID can run once in its active session. A restart invalidates the in-memory page session.",
                    ],
                    Bullets =
                    [
                        "Send only a declared page action.",
                        "Send only fields from that action schema.",
                        "Use a new message ID for each action attempt.",
                        "Treat a rejected or expired session as terminal.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Plugin automations", "plugin-development/automations"),
                new SiteLink("Testing and releases", "plugin-development/test-publish"),
            ],
        };
    }
}
