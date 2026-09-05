namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginHandlerPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development/handlers",
            Eyebrow = "Plugin development",
            Title = "Plugin handlers",
            Summary =
                "Handlers connect manifest declarations to Lua functions. Plugins can declare the handler types below.",
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Commands and events.",
                        "Schedules and webhooks.",
                        "HTTP actions and page actions.",
                        "Pages and automation handlers.",
                    ],
                    Heading = "Handler modules",
                    Paragraphs =
                    [
                        "A handler module returns a table of functions. Each declared operation identifies one module and one function in that table.",
                    ],
                    Code = """
                        local blokebot = require("blokebot")

                        local handlers = {
                          handle_command = function(input)
                            blokebot.responses.chat("Received " .. input.route)
                            return nil
                          end,
                        }

                        return handlers
                        """,
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The event handler receives the event identity.",
                        "The event handler receives the source.",
                        "The event handler receives the timestamp.",
                    ],
                    Heading = "Feature handlers",
                    LegacyAnchor = "commands-events-and-schedules",
                    Facts =
                    [
                        new(
                            "Command",
                            "The handler receives the normalized route and ordered arguments for the current channel."
                        ),
                        new("Typed Twitch event", "The handler receives the typed event data."),
                        new(
                            "Raw Twitch event",
                            "The handler receives the declared EventSub identity and a validated raw event map."
                        ),
                        new(
                            "BlokeBot event",
                            "The handler receives the event identity and source name."
                        ),
                        new(
                            "Schedule",
                            "The handler receives the input that BlokeBot stored when the feature created the schedule."
                        ),
                    ],
                    Note =
                        "Set twitchReady to true only if the handler requires current Twitch scope and EventSub readiness.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "An HTTP action receives method from the fixed authenticated endpoint.",
                        "An HTTP action receives headers from the fixed authenticated endpoint.",
                        "An HTTP action receives bodyBase64 from the fixed authenticated endpoint.",
                    ],
                    Heading = "HTTP actions and page actions",
                    Paragraphs =
                    [
                        "A page action receives only the exact fields in its manifest declaration.",
                    ],
                    Code = """
                        [[features.dispatch.actions]]
                        kind = "page"
                        id = "review-item"
                        module = "main"
                        operation = "review_item"
                        inputs = [
                          { id = "item_id", name = "Item ID", valueKind = "number", required = true },
                          { id = "decision", name = "Decision", valueKind = "string", required = true },
                        ]

                        [[features.dispatch.actions]]
                        kind = "http"
                        id = "refresh-item"
                        module = "main"
                        operation = "refresh_item"
                        """,
                    Note =
                        "If a task needs HTTP and page entry points, declare two actions. Declare two handler operations.",
                },
                new SiteGuideSection
                {
                    Heading = "Webhooks",
                    Paragraphs =
                    [
                        "A webhook either accepts public requests or calls one plugin authentication handler before it calls the webhook handler.",
                        "BlokeBot checks authentication and handles the webhook within one request. A plugin or feature change cancels both steps.",
                    ],
                    Bullets =
                    [
                        "Use public authentication only when the webhook intentionally accepts public requests.",
                        "Return false from the authentication handler to reject a request.",
                        "Do not create an identity from request headers.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "A migration handler receives migrationId.",
                        "A migration handler receives fromVersion.",
                        "A migration handler receives toVersion.",
                    ],
                    Heading = "Migrations",
                    Paragraphs =
                    [
                        "It can use storage and redaction-safe diagnostics.",
                        "Before activation, BlokeBot runs one selected migration chain. If a migration fails, BlokeBot faults the selected installation.",
                    ],
                    Code = """
                        [[migrations]]
                        id = "schema-v1"
                        fromVersion = "0.0.0"
                        toVersion = "0.1.0"
                        module = "main"
                        entryPoint = "migrate"
                        """,
                    Note =
                        "After durable migration starts, BlokeBot does not restore the old selected package.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The cancellation rule applies to chat.",
                        "The cancellation rule applies to schedules.",
                        "The cancellation rule applies to HTTP.",
                        "The cancellation rule applies to SQLite.",
                        "The cancellation rule applies to other effects.",
                    ],
                    Heading = "Cancellation",
                    Paragraphs =
                    [
                        "Cancellation stops a coroutine that waits for a host result. A late result does not resume the coroutine.",
                        "Cancellation does not undo an effect that completed first.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Host API", "plugin-development/host-api"),
                new SiteLink("Plugin pages", "plugin-development/pages"),
            ],
        };
    }
}
