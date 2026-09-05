namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreatePluginHostApiPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugin-development/host-api",
            Eyebrow = "Plugin development",
            Title = "Host API",
            Summary = "Use the API for the current invocation.",
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "For each API call, BlokeBot checks context.",
                        "For each API call, BlokeBot checks arguments.",
                        "For each API call, BlokeBot checks the result.",
                        "For each API call, BlokeBot checks payload limits.",
                        "For each API call, BlokeBot checks cancellation.",
                        "For each API call, BlokeBot checks active plugin state.",
                        "The returned context can be the current installation context.",
                        "The returned context can be the current channel context.",
                        "The returned context can be the current automation context.",
                        "The returned context can be the current migration context.",
                        "The returned context can be the current page context.",
                    ],
                    Heading = "Invocation APIs",
                    LegacyAnchor = "context-settings-and-diagnostics",
                    Facts =
                    [
                        new("blokebot.context.current()", "Returns the current context."),
                        new(
                            "blokebot.settings.installation()",
                            "Returns the declared values for the current plugin installation."
                        ),
                        new(
                            "blokebot.settings.feature()",
                            "Returns the declared values for the current host feature."
                        ),
                        new(
                            "blokebot.diagnostics.log(level, message)",
                            "Writes one redaction-safe diagnostic message."
                        ),
                    ],
                    Note =
                        "Migration handlers cannot read settings. They can read migration context and use plugin storage.",
                },
                new SiteGuideSection
                {
                    Heading = "Channel messages and effects",
                    Facts =
                    [
                        new(
                            "blokebot.responses.chat(message)",
                            "Replies in the channel for the current context."
                        ),
                        new(
                            "blokebot.responses.whisper(message)",
                            "Replies privately to the actor in the current context."
                        ),
                        new(
                            "blokebot.chat.send(message)",
                            "Sends one message to the channel for the current context."
                        ),
                        new(
                            "blokebot.overlay.play_cue(target_id, cue_id)",
                            "Requests one cue for the current channel."
                        ),
                        new(
                            "blokebot.points.add(viewer, amount, reason)",
                            "Adds a non-negative amount and returns the new balance."
                        ),
                        new(
                            "blokebot.twitch.create_marker(description)",
                            "Creates one stream marker for the channel in the current context."
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Schedules",
                    Facts =
                    [
                        new(
                            "blokebot.schedules.once(handler_id, due_at, input)",
                            "Creates one schedule for the current enabled feature."
                        ),
                        new(
                            "blokebot.schedules.recurring(handler_id, due_at, interval_seconds, input)",
                            "Creates one recurring schedule for the current enabled feature."
                        ),
                        new(
                            "blokebot.schedules.cancel(schedule_id)",
                            "Cancels one schedule that the current enabled feature owns."
                        ),
                    ],
                    Note =
                        "The handler ID must match a declared schedule handler for the current feature.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The storage API rejects attached databases.",
                        "The storage API rejects unsafe file operations.",
                        "The storage API rejects virtual tables.",
                        "The storage API rejects multiple statements.",
                    ],
                    Heading = "Private SQLite database",
                    Paragraphs =
                    [
                        "Each plugin owns one private SQLite database. The plugin identity in the current context determines the database.",
                        "Use context.current to store a host ID with rows that belong to a channel. The storage API does not partition rows by host ID.",
                    ],
                    Facts =
                    [
                        new(
                            "blokebot.storage.execute(sql, parameters)",
                            "Executes one supported statement and returns the affected row count."
                        ),
                        new(
                            "blokebot.storage.query(sql, parameters)",
                            "Runs one supported query and returns typed row maps."
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The result can be a typed response.",
                        "The result can be a typed rejection.",
                        "The result can be a typed failure.",
                        "The method and URI.",
                        "The headers and body.",
                        "The response and redirect.",
                        "The duration and cancellation.",
                        "Plugin concurrency.",
                    ],
                    Heading = "HTTP requests",
                    Paragraphs = ["http.send enforces the host HTTP policy for the items below."],
                    Code = """
                        local outcome = blokebot.http.send({
                          method = "POST",
                          url = "https://example.invalid/items",
                          headers = { ["content-type"] = "application/json" },
                          body = "{}",
                        })

                        if outcome.kind == "response" then
                          return outcome.status
                        end
                        """,
                    Note = "If the caller cancels the request, the current call ends.",
                },
            ],
            Next =
            [
                new SiteLink("Plugin pages", "plugin-development/pages"),
                new SiteLink("Plugin automations", "plugin-development/automations"),
            ],
        };
    }
}
