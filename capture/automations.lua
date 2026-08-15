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
width = 390
height = 844

[matrix]
theme = ["light", "dark"]
mode = ["grid", "list"]
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
        const select = [...document.querySelectorAll(".automation-canvas-tools select")]
          .find((candidate) => [...candidate.options].some((option) => option.value === "Smooth"));
        select.value = "Smooth";
        select.dispatchEvent(new Event("change", { bubbles: true }));
        return true;
      })()
    ]=]))
    viset.page.wait_for(
      viset.javascript([=[
        document.querySelector('[data-automation-canvas-shell][data-edge-style="smooth"]') !== null
          && [...document.querySelectorAll('[data-automation-edge] .automation-edge')]
            .every((path) => path.getAttribute("d").includes(" C "))
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
        const node = [...document.querySelectorAll("[data-automation-node]")]
          .find((candidate) => candidate.querySelector("strong")?.textContent.trim() === "Condition");
        node.click();
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
