--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/chat-tools"
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
width = 720
height = 1600

[matrix]
theme = ["light", "dark"]
view = [
  "chat-tools-all-disabled",
  "chat-tools-enabled",
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

local states = {
  ["chat-tools-all-disabled"] = { features = "all-disabled", liveness = "offline" },
  ["chat-tools-enabled"] = { features = "mixed", liveness = "live" },
}

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local view = viset.context.axes.view
  local state = states[view]

  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  settle("/")

  viset.page.evaluate(
    viset.javascript([=[
      async ({ features, liveness }) => {
        const post = path => fetch(path, { method: "POST" });
        await post("/simulation/commands/round/none");
        await post("/simulation/commands/giveaway/inactive");
        await post(`/simulation/commands/liveness/${liveness}`);
        await post(`/simulation/commands/features/${features}`);
        await new Promise(resolve => setTimeout(resolve, 500));
        return true;
      }
    ]=]),
    { features = state.features, liveness = state.liveness }
  )
  viset.sleep("2s")

  viset.page.navigate(base_url .. "/host?simulationTheme=" .. theme)
  settle("/host")
  viset.sleep("750ms")

  viset.page.evaluate(
    viset.javascript([=[
      () => {
        document.querySelector("#chat-tools")?.scrollIntoView({ block: "start" });
        window.scrollBy(0, -12);
        return true;
      }
    ]=])
  )
  viset.sleep("750ms")
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
