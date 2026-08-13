--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media"
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
width = 1180
height = 720

[devices.phone]
mobile = true
touch = true
device_scale = 1.0

[devices.phone.viewport]
width = 390
height = 844

[matrix]
theme = ["light", "dark"]
view = [
  "points-settings",
  "guessing-leaderboard",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_POINTS_GUESSING_PORT") or "43222"
local base_url = "http://127.0.0.1:" .. port
local server = viset.process.start({
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

local readiness = {
  ["points-settings"] = {
    path = "/points/settings",
    expression = [[document.body.innerText.includes("Points settings")]],
  },
  ["guessing-leaderboard"] = {
    path = "/guessing/leaderboard/samplechannel",
    expression = [[document.body.innerText.includes("Guessing leaderboard")]],
  },
}

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local view = viset.context.axes.view
  local expected = readiness[view]
  if expected == nil then
    error("No capture readiness is registered for " .. view)
  end
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=" .. view .. "&theme=" .. theme)
  local ready_expression = ([=[
    window.location.pathname === %q &&
      document.body.innerText.includes("Sample Channel") &&
      (%s) &&
      getComputedStyle(document.querySelector("main")).opacity === "1"
  ]=]):format(expected.path, expected.expression)
  viset.page.wait_for(viset.javascript(ready_expression), "20s")
  viset.sleep("350ms")
  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
