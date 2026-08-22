--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/configuration-transfer"
output = "{device}-{theme}-{state}.png"
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
state = ["export", "upload", "review", "conflict", "success", "failed"]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_CAPTURE_PORT") or "5478"
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
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "3s" })
end)
local server = nil
if not reachable then
  server = startServer()
end

local browserActions = require("configuration-transfer-browser-actions")

local function waitForPage(theme)
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === "/configuration-transfer"
        && document.documentElement.dataset.theme === %q
        && document.querySelector(".task-panel") !== null
        && document.querySelector("#components-reconnect-modal")?.open !== true
    ]=]):format(theme)),
    "40s"
  )
end

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local state = viset.context.axes.state
  local suffix = viset.context.device.name .. "-" .. theme
  local viewportWidth = viset.context.device.name == "phone" and 495 or 1920
  local viewportHeight = viset.context.device.name == "phone" and 1100 or 1080

  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=configuration-transfer&theme=" .. theme)
  waitForPage(theme)
  viset.sleep("600ms")

  if state ~= "export" then
    viset.page.evaluate(browserActions.openImport, {})
    viset.page.wait_for(
      "document.getElementById('configuration-transfer-json') !== null",
      "20s"
    )
  end

  if state == "export" then
    viset.page.evaluate(
      browserActions.prepareExport,
      { viewportWidth = viewportWidth, viewportHeight = viewportHeight }
    )
  end

  if state ~= "export" and state ~= "upload" then
    viset.page.evaluate(browserActions.loadDocument, { state = state, suffix = suffix })
    viset.page.wait_for(
      "document.getElementById('configuration-transfer-preview')?.disabled === false",
      "20s"
    )
    viset.page.evaluate(browserActions.previewImport, {})
    viset.page.wait_for(
      "document.querySelector('.live-round-summary') !== null",
      "40s"
    )
  end

  if state == "conflict" then
    viset.page.evaluate(browserActions.focusConflict, { viewportWidth = viewportWidth })
  elseif state == "success" then
    viset.page.evaluate(browserActions.resolveConflicts, {})
    viset.page.wait_for(
      "document.getElementById('configuration-transfer-apply')?.disabled === false",
      "20s"
    )
    viset.page.evaluate(browserActions.applyImport, {})
    viset.page.wait_for(
      "document.querySelector('.page-state[data-page-state=\"success\"]') !== null",
      "40s"
    )
  elseif state == "failed" then
    viset.page.evaluate(browserActions.selectEnablement, {})
    viset.sleep("300ms")
    viset.page.wait_for(
      "document.getElementById('configuration-transfer-apply')?.disabled === false",
      "20s"
    )
    viset.page.evaluate(browserActions.applyImport, {})
    viset.page.wait_for(
      "document.querySelector('.activation-row[data-status=\"failed\"]') !== null",
      "40s"
    )
  end

  if state ~= "conflict" then
    viset.page.evaluate("window.scrollTo(0, 0); true")
  end
  viset.sleep("500ms")
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
