--[[
# viset
version = 1
output_root = "../../agent-planning/projects/blokebot/investigations/20260901-milestone-015-v0.15.0-viewer-portal/evidence/BLOKEBOT-277/captures"
output = "{device}-{theme}-{view}.png"
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

[devices.laptop]
mobile = false
touch = false
device_scale = 1.0

[devices.laptop.viewport]
width = 1920
height = 1080

[devices.phone]
mobile = true
touch = true
device_scale = 1.0

[devices.phone.viewport]
width = 495
height = 1100

[matrix]
theme = ["light", "dark"]
view = ["portal-anonymous", "portal-authenticated", "bingo", "raid", "bounties", "community", "competitions", "moments", "moments-stream", "queue", "requests", "collective", "passport", "points", "guessing"]
]]

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

local routes = {
  ["portal-anonymous"] = "/channel/samplechannel",
  ["portal-authenticated"] = "/channel/samplechannel",
  ["bingo"] = "/bingo/samplechannel",
  ["raid"] = "/raid/samplechannel",
  ["bounties"] = "/bounties/samplechannel",
  ["community"] = "/community/samplechannel",
  ["competitions"] = "/competitions/samplechannel",
  ["moments"] = "/moments/samplechannel",
  ["moments-stream"] = "/moments/samplechannel/streams/stream-0001",
  ["queue"] = "/queues/samplechannel/main",
  ["requests"] = "/requests/samplechannel/requests",
  ["collective"] = "/collectives/samplechannel/3f78b947-a0f8-4872-ae3b-a876a27e58a0",
  ["passport"] = "/passport/samplechannel/nightowl",
  ["points"] = "/points/leaderboard/samplechannel",
  ["guessing"] = "/guessing/leaderboard/samplechannel",
}

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local view = viset.context.axes.view
  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "90s" })
  local destination = routes[view] .. "?simulationTheme=" .. theme
  if view == "portal-authenticated" then
    destination = "/simulation/login?view=viewer-portal&theme=" .. theme
  end
  viset.page.navigate(base_url .. destination)
  viset.page.wait_for(viset.javascript([=[
    document.readyState === "complete"
      && document.querySelector("main.portal__main") !== null
      && document.fonts.status === "loaded"
      && !document.querySelector("#components-reconnect-modal.components-reconnect-show")
  ]=]), "30s")
  viset.sleep("1s")
  viset.snapshot()
end)
if server ~= nil then viset.process.stop(server) end
if not succeeded then error(failure, 0) end
