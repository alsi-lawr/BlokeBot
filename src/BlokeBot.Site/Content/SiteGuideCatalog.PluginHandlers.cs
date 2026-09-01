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
                "Handlers connect manifest declarations to Lua functions. Plugins can declare commands, events, schedules, webhooks, HTTP actions, page actions, pages, and automation handlers.",
            Sections =
            [
                new SiteGuideSection
                {
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
                    Heading = "Commands, events, and schedules",
                    Facts =
                    [
                        new(
                            "Command",
                            "The handler receives the normalized route and ordered arguments for the current channel."
                        ),
                        new(
                            "Typed Twitch event",
                            "The handler receives the event identity, source, and timestamp."
                        ),
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
                    Heading = "HTTP actions and page actions",
                    Paragraphs =
                    [
                        "An HTTP action receives method, headers, and bodyBase64 from the fixed authenticated endpoint.",
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
                        "If a task needs HTTP and page entry points, declare two actions and two handler operations.",
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
                    Heading = "Migrations",
                    Paragraphs =
                    [
                        "A migration handler receives migrationId, fromVersion, and toVersion. It can use storage and redaction-safe diagnostics.",
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
                    Heading = "Cancellation",
                    Paragraphs =
                    [
                        "Cancellation stops a coroutine that waits for a host result. A late result does not resume the coroutine.",
                        "Cancellation does not undo an effect that completed first. This rule applies to chat, schedules, HTTP, SQLite, and other effects.",
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
