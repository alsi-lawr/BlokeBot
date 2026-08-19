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
height = 1000

[devices.wide]
mobile = false
touch = false
device_scale = 1.0

[devices.wide.viewport]
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

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local mode = viset.context.axes.mode
  local device = viset.context.device.name
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
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
  if mode == "list" then
    viset.page.evaluate(viset.javascript([=[
      document.querySelector('[data-automation-mode="list"]').click();
      true
    ]=]))
    viset.page.wait_for(
      viset.javascript([=[
        document.querySelector("[data-automation-list]") !== null
      ]=]),
      "20s"
    )
  else
    viset.page.wait_for(
      viset.javascript([=[
        document.querySelector('[data-automation-canvas-ready="true"]') !== null
      ]=]),
      "20s"
    )
    viset.page.evaluate(viset.javascript([=[
      (() => {
        const select = [...document.querySelectorAll(".automation-workspace-toolbar select")]
          .find((candidate) => [...candidate.options].some((option) => option.value === "Smooth"));
        select.value = "Smooth";
        select.dispatchEvent(new Event("change", { bubbles: true }));
        return true;
      })()
    ]=]))
    viset.page.wait_for(
      viset.javascript([=[
        (() => {
          if (document.querySelector('[data-automation-canvas-shell][data-edge-style="smooth"]') === null) {
            return false;
          }
          const routes = [...document.querySelectorAll('[data-automation-edge] .automation-edge')]
            .map((path) => path.getAttribute("d") || "");
          if (routes.length === 0 || routes.some((route) => route.trim() === "")) {
            return false;
          }
          // Smooth mode falls back to the angular skeleton for a path the spline cannot
          // drape, so require curves on the majority rather than on every route.
          return routes.filter((route) => route.includes(" C ")).length > routes.length / 2;
        })()
      ]=]),
      "20s"
    )
  end
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
  if device == "wide" and mode == "grid" then
    viset.page.evaluate(viset.javascript([=[
      (() => {
        // Nodes carry author-chosen names, so select the first Condition by kind.
        const node = [...document.querySelectorAll('[data-automation-node][data-node-kind="control"]')]
          .find((candidate) => candidate.querySelector("[data-automation-node-select]") !== null);
        node.querySelector("[data-automation-node-select]").click();
        return true;
      })()
    ]=]))
    viset.page.wait_for(
      viset.javascript([=[
        (() => {
          const inspector = document.querySelector("[data-automation-inspector]");
          return inspector !== null
            && inspector.getAnimations().every((animation) => animation.playState === "finished");
        })()
      ]=]),
      "20s"
    )
  end
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
