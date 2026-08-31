local blokebot = require("blokebot")

local function map()
  return {}
end

local function host_calls()
  local context = blokebot.context.current()
  local installation_settings = blokebot.settings.installation()
  local feature_settings = blokebot.settings.feature()
  blokebot.diagnostics.log("information", "author showcase")
  blokebot.responses.chat("showcase response")
  blokebot.responses.whisper("showcase whisper")
  blokebot.chat.send("showcase chat")
  blokebot.overlay.play_cue(
    "00000000-0000-0000-0000-000000000001",
    "00000000-0000-0000-0000-000000000002"
  )
  local points = blokebot.points.add("viewer", "1", "showcase")
  blokebot.twitch.create_marker("showcase marker")
  local schedule = blokebot.schedules.once("daily", "2026-08-26T20:00:00Z", map())
  blokebot.schedules.cancel(schedule)
  local recurring = blokebot.schedules.recurring("daily", "2026-08-26T20:00:00Z", 300, map())
  blokebot.schedules.cancel(recurring)
  local changed = blokebot.storage.execute("CREATE TABLE fixture(message TEXT)", map())
  local rows = blokebot.storage.query("SELECT message FROM fixture", map())
  local response = blokebot.http.send({ method = "GET", url = "https://example.invalid/showcase" })
  return {
    plugin_id = context.pluginId,
    endpoint = installation_settings["metadata-endpoint"],
    interval = feature_settings["publish-interval"],
    points = points,
    changed = changed,
    rows = rows,
    status = response.status,
  }
end

---@type ExamplesAuthorShowcaseMainHandlers
local handlers = {
  setup = function()
    blokebot.diagnostics.log("information", "setup")
    return "ready"
  end,
  migrate = function()
    blokebot.diagnostics.log("information", "migration")
    return "migrated"
  end,
  host_calls = host_calls,
  on_event = function() return "event" end,
  on_schedule = function() return "schedule" end,
  on_webhook = function() return { status = 202 } end,
  on_action = function() return "action" end,
  storage_roundtrip = function()
    blokebot.storage.execute("INSERT INTO fixture(message) VALUES ($message)", { message = "hello" })
    return blokebot.storage.query("SELECT message FROM fixture", map())
  end,
  render_page = function() return { title = "Author showcase", body = "Generated locally" } end,
  automation_source = function() return { message = "automation" } end,
  automation_action = function() return "automation" end,
  cancel_wait = function() return blokebot.schedules.once("daily", "2026-08-26T20:00:00Z", map()) end,
  host_failure = function() return blokebot.responses.chat("reject fixture") end,
}

return handlers
