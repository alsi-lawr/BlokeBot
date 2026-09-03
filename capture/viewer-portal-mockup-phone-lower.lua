--[[
# viset
version = 1
output_root = "../../agent-planning/projects/blokebot/investigations/20260901-milestone-015-v0.15.0-viewer-portal/evidence/BLOKEBOT-274/captures"
output = "{device}-{theme}-{view}-lower.png"
frame = "builtin:auto"
browser_arguments = [
  "--disable-background-networking",
  "--disable-background-mode",
  "--disable-component-update",
  "--disable-default-apps",
  "--disable-sync",
  "--force-prefers-reduced-motion",
  "--host-resolver-rules=MAP * 0.0.0.0, EXCLUDE 127.0.0.1",
  "--hide-scrollbars",
  "--metrics-recording-only",
  "--password-store=basic",
  "--use-mock-keychain",
]

[devices.phone]
mobile = true
touch = true
device_scale = 1.0

[devices.phone.viewport]
width = 495
height = 1100

[matrix]
theme = ["light", "dark"]
view = ["populated-anonymous", "populated-authenticated", "auth-unavailable"]
]]

-- BLOKEBOT-274 design evidence: the lower half of the phone portal mockup, scrolled to the
-- personal section so the "You" and "Recent activity" regions are visible.

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_CAPTURE_PORT") or "43217"
local base_url = "http://127.0.0.1:" .. port
local function startServer()
  return viset.process.start({
    file = os.getenv("BLOKEBOT_DOTNET") or "dotnet",
    arguments = {
      "run",
      "--project",
      repo_root .. "/src/BlokeBot.Simulation/BlokeBot.Simulation.csproj",
      "--configuration",
      "Release",
      "--no-build",
      "--no-launch-profile",
      "--",
      "--urls",
      base_url,
    },
    working_directory = repo_root,
    environment = {
      DOTNET_CLI_TELEMETRY_OPTOUT = "1",
      TZ = "UTC",
    },
  })
end

local reachable = pcall(function()
  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "3s" })
end)
local server = nil
if not reachable then
  server = startServer()
end

local targets = {
  ["populated-anonymous"] = { state = "populated", viewer = "anonymous" },
  ["populated-authenticated"] = { state = "populated", viewer = "authenticated" },
  ["auth-unavailable"] = { state = "populated", viewer = "unavailable" },
}

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local target = targets[viset.context.axes.view]

  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "90s" })
  viset.page.navigate(
    base_url
      .. "/simulation/portal-mockup?state="
      .. target.state
      .. "&viewer="
      .. target.viewer
      .. "&theme="
      .. theme
  )
  viset.page.wait_for(
    viset.javascript([=[
      document.readyState === "complete"
        && document.querySelector("#portal-you") !== null
        && document.fonts.status === "loaded"
    ]=]),
    "30s"
  )

  viset.page.evaluate(
    'document.querySelector("#portal-you").scrollIntoView({ block: "start" }); window.scrollBy(0, -72); true'
  )

  viset.sleep("350ms")
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
