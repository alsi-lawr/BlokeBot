local function map()
  return {}
end

local function host_calls()
  blokebot.host.call("diagnostics", "log", "information", "author showcase")
  blokebot.host.call("responses", "chat", "showcase response")
  blokebot.host.call("responses", "whisper", "showcase whisper")
  blokebot.host.call("chat", "send", "showcase chat")
  blokebot.host.call("overlay", "play-cue", "showcase", "{}")
  local points = blokebot.host.call("points", "add", "viewer", "1", "showcase")
  blokebot.host.call("twitch", "create-marker", "showcase marker")
  local schedule = blokebot.host.call("schedules", "once", "daily", "2026-08-26T20:00:00Z", map())
  blokebot.host.call("schedules", "cancel", schedule)
  local changed = blokebot.host.call("storage", "execute", "CREATE TABLE fixture(message TEXT)", map())
  local rows = blokebot.host.call("storage", "query", "SELECT message FROM fixture", map())
  local response = blokebot.host.call("http", "send", { method = "GET", url = "https://example.invalid/showcase" })
  return { points = points, changed = changed, rows = rows, status = response.status }
end

return {
  setup = function()
    blokebot.host.call("diagnostics", "log", "information", "setup")
    return "ready"
  end,
  migrate = function()
    blokebot.host.call("diagnostics", "log", "information", "migration")
    return "migrated"
  end,
  host_calls = host_calls,
  on_event = function() return "event" end,
  on_schedule = function() return "schedule" end,
  on_webhook = function() return { status = 202 } end,
  on_action = function() return "action" end,
  storage_roundtrip = function()
    blokebot.host.call("storage", "execute", "INSERT INTO fixture(message) VALUES ($message)", { message = "hello" })
    return blokebot.host.call("storage", "query", "SELECT message FROM fixture", map())
  end,
  render_page = function() return { title = "Author showcase", body = "Generated locally" } end,
  automation_source = function() return { message = "automation" } end,
  automation_action = function() return "automation" end,
  cancel_wait = function() return blokebot.host.call("schedules", "once", "daily", "2026-08-26T20:00:00Z", map()) end,
}
