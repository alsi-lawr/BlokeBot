--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/automations"
output = "{device}-{theme}-{mode}-visual-automations.png"
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
height = 1180

[devices.wide]
mobile = false
touch = false
device_scale = 1.0

[devices.wide.viewport]
width = 1920
height = 1180

[devices.phone]
mobile = true
touch = true
device_scale = 1.0

[devices.phone.viewport]
width = 390
height = 1250

[matrix]
theme = ["light", "dark"]
mode = ["grid", "list"]
]]

local repo_root = viset.script.directory .. "/.."
local configured_port = os.getenv("BLOKEBOT_CAPTURE_PORT")
local device_offsets = { desktop = 0, wide = 4, phone = 8 }
local theme_offsets = { light = 0, dark = 2 }
local mode_offsets = { grid = 0, list = 1 }
local port = configured_port or tostring(
  43221
    + device_offsets[viset.context.device.name]
    + theme_offsets[viset.context.axes.theme]
    + mode_offsets[viset.context.axes.mode]
)
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
      BLOKEBOT_AUTOMATION_AUTHORING_FIXTURE = "1",
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

local function js(code)
  return viset.page.evaluate(viset.javascript("(() => {" .. code .. "})()"))
end
local function wait(code)
  viset.page.wait_for(viset.javascript(code), "40s")
end

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local mode = viset.context.axes.mode
  local phone = viset.context.device.name == "phone"
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=automations&theme=" .. theme)
  wait("document.querySelector('[data-automation-canvas-ready=\"true\"]') !== null")
  if mode == "list" then
    js([[document.querySelector('#automation-library-subflows-tab').click();]])
    wait("document.querySelector('.automation-flow-rail [data-automation-subflow-id]') !== null")
    js([[ [...document.querySelectorAll('.automation-flow-rail [data-automation-subflow-id]')].find(e => e.querySelector('strong')?.textContent === 'Welcome message').click(); ]])
    wait("document.querySelector('#automation-node-display-alias')?.value === 'Entry'")
    js([[ [...document.querySelectorAll('.automation-mode-switch button')].find(e => e.textContent.includes('List')).click(); ]])
    wait("document.querySelector('[data-editor-mode=\"list\"]') !== null")
    js([[ [...document.querySelectorAll('[data-automation-list-node]')].find(e => e.querySelector('strong')?.textContent === 'Exit').click(); ]])
    wait("document.querySelector('.automation-return-values-heading') !== null")
    if phone then
      js([[document.querySelector('.automation-mobile-selection-summary button').click();]])
      wait("document.querySelector('.automation-inspector--mobile-open') !== null")
    end
    wait("getComputedStyle(document.querySelector('.automation-inspector')).opacity === '1'")
  else
    js([[ [...document.querySelectorAll('.automation-flow-item')].find(e => e.querySelector('strong')?.textContent === 'Welcome command').click(); ]])
    wait("document.querySelector('#automation-flow-name')?.value === 'Welcome command'")
    js([[document.querySelector('[data-automation-test-flow]').click();]])
    wait("document.querySelector('[data-automation-run-scenario]') !== null")
    js([[document.querySelector('[data-automation-run-scenario]').click();]])
    wait("document.querySelector('[data-trace-sequence]') !== null")
    if phone then
      js([[document.querySelector('[aria-label=\"Close authoring panel\"]').click();]])
      wait("document.querySelector('[data-automation-authoring]') === null")
      js([[const trace = document.querySelector('[data-automation-trace]'); trace.scrollTop = 0; trace.querySelector('ol').scrollTop = 0; trace.scrollIntoView({block:'end'});]])
    end
  end
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
