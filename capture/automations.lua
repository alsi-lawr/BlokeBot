--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/automations"
output = "{device}-{theme}-visual-automations.png"
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

[devices.desktop]
mobile = false
touch = false
device_scale = 1.0

[devices.desktop.viewport]
width = 1440
height = 1000

[devices.phone]
mobile = true
touch = true
device_scale = 1.0

[devices.phone.viewport]
width = 390
height = 844

[matrix]
theme = ["light", "dark"]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_CAPTURE_PORT") or "43221"
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
  viset.http.wait({ url = base_url .. "/app.css", timeout = "3s" })
end)
local server = nil
if not reachable then
  server = startServer()
end

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  viset.http.wait({ url = base_url .. "/app.css", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=automations&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript([=[
      location.pathname === "/automations"
        && document.querySelector("[data-automation-editor-page]") !== null
        && document.querySelector("[data-automation-canvas]") !== null
        && document.querySelector("#components-reconnect-modal")?.open !== true
    ]=]),
    "40s"
  )
  viset.page.evaluate(viset.javascript([=[
    document.querySelector("[data-automation-test-flow]").click();
    true
  ]=]))
  viset.page.wait_for(
    viset.javascript([=[
      document.querySelector('[data-automation-run-kind="sample"]') !== null
    ]=]),
    "40s"
  )
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
