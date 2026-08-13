--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/community"
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
view = ["moments", "request-boards", "play-with-viewers"]
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

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local view = viset.context.axes.view
  local device = viset.context.device.name
  local targets = {
    ["request-boards"] = {
      laptop = {
        path = "/requests",
      },
      phone = {
        path = "/requests/samplechannel/requests",
      },
    },
    ["play-with-viewers"] = {
      laptop = {
        path = "/queues",
      },
      phone = {
        path = "/queues/samplechannel/main",
      },
    },
    ["moments"] = {
      laptop = {
        path = "/moments",
      },
      phone = {
        path = "/moments/samplechannel/streams/stream-0001",
      },
    },
  }
  local target = targets[view][device]

  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript([=[
      location.pathname === "/"
        && document.querySelector("main") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]),
    "30s"
  )

  viset.page.evaluate(
    viset.javascript([=[
      (async () => {
        const post = path => fetch(path, { method: "POST" });
        await post("/simulation/commands/features/all-enabled");
        await post("/simulation/commands/liveness/production");
        await new Promise(resolve => setTimeout(resolve, 400));
        return true;
      })()
    ]=])
  )

  viset.page.navigate(base_url .. target.path .. "?simulationTheme=" .. theme)
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === %q
        && document.querySelector("main") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]):format(target.path)),
    "30s"
  )

  viset.sleep("350ms")
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
