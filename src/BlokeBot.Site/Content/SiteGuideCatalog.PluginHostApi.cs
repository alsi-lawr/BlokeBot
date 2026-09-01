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
            Summary =
                "For each API call, BlokeBot checks the context, arguments, result, payload limits, cancellation, and active plugin state.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Context, settings, and diagnostics",
                    Facts =
                    [
                        new(
                            "blokebot.context.current()",
                            "Returns the current installation, channel, automation, migration, or page context."
                        ),
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
                    Note =
                        "The storage API rejects attached databases, unsafe file operations, virtual tables, and multiple statements.",
                },
                new SiteGuideSection
                {
                    Heading = "HTTP requests",
                    Paragraphs =
                    [
                        "http.send enforces the host HTTP policy for the method, URI, headers, body, response, redirect, duration, cancellation, and plugin concurrency.",
                    ],
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
                    Note =
                        "The result is a typed response, rejection, or failure. If the caller cancels the request, the current call ends.",
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
