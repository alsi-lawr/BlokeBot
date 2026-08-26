--[[
# viset
version = 1
output_root = "output/plugin-admin"
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
state = ["installed", "fault", "no-snapshot", "removal-confirmation"]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_CAPTURE_PORT") or "5480"
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
  viset.http.wait({ url = base_url, timeout = "3s" })
end)
local server = nil
if not reachable then
  server = startServer()
end

local function waitForPage(theme)
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === "/admin"
        && location.hash === "#plugins"
        && document.documentElement.dataset.theme === %q
        && document.querySelector("[data-plugin-admin]") !== null
        && document.querySelector("#components-reconnect-modal")?.open !== true
    ]=]):format(theme)),
    "40s"
  )
end

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local state = viset.context.axes.state

  viset.http.wait({ url = base_url, timeout = "90s" })
  viset.page.navigate(
    base_url .. "/simulation/plugin-admin/" .. state .. "/view?theme=" .. theme
  )
  waitForPage(theme)

  if state == "removal-confirmation" then
    viset.page.evaluate(
      viset.javascript([=[
        (() => {
          document.querySelector("[data-installed-plugin] .plugin-admin__danger-button")?.click();
          return true;
        })()
      ]=])
    )
    viset.page.wait_for(
      "document.querySelector('[data-plugin-confirmation-dialog]') !== null",
      "20s"
    )
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
