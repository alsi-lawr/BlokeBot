--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/commands"
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
view = [
  "viewer-command-catalog",
]
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

local function settle(path)
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === %q
        && document.querySelector("main") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
        && document.querySelector("#components-reconnect-modal")?.open !== true
    ]=]):format(path)),
    "40s"
  )
end

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme

  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  settle("/")

  viset.page.evaluate(
    viset.javascript([=[
      (async () => {
        const post = path => fetch(path, { method: "POST" });
        await post("/simulation/commands/round/open");
        await post("/simulation/commands/giveaway/active");
        await post("/simulation/commands/liveness/live");
        await post("/simulation/commands/features/all-enabled");
        await new Promise(resolve => setTimeout(resolve, 500));
        return true;
      })()
    ]=])
  )
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })

  viset.page.navigate(base_url .. "/host?simulationTheme=" .. theme)
  settle("/host")
  viset.sleep("750ms")

  viset.page.evaluate(viset.javascript([=[
    (async () => {
      const stageHeader = title => [...document.querySelectorAll("button.studio-stage__header")]
        .find(candidate => candidate.querySelector(".studio-stage__title")?.textContent.trim()
          === title);
      const inventory = () => document.querySelector("[data-fold='command-inventory'] button");
      const commands = stageHeader("Commands");
      if (commands?.getAttribute("aria-expanded") !== "true") commands?.click();
      inventory()?.click();
      await new Promise(resolve => setTimeout(resolve, 1000));
      if (inventory()?.getAttribute("aria-expanded") !== "true") inventory()?.click();
      stageHeader("Commands")?.closest("section")?.scrollIntoView({ block: "start" });
      window.scrollBy(0, -12);
      return true;
    })()
  ]=]))
  viset.sleep("750ms")
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
