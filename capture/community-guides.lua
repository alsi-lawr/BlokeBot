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
local port = os.getenv("BLOKEBOT_COMMUNITY_GUIDES_PORT") or "5460"
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

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local view = viset.context.axes.view
  local device = viset.context.device.name
  local targets = {
    ["request-boards"] = {
      laptop = {
        path = "/requests",
        ready = "Game night requests",
      },
      phone = {
        path = "/requests/samplechannel/requests",
        ready = "Submit a request",
      },
    },
    ["play-with-viewers"] = {
      laptop = {
        path = "/queues",
        ready = "Edit queue",
      },
      phone = {
        path = "/queues/samplechannel/main",
        ready = "Join the queue",
      },
    },
    ["moments"] = {
      laptop = {
        path = "/moments",
        ready = "Community clutch save",
      },
      phone = {
        path = "/moments/samplechannel/streams/stream-0001",
        ready = "Community clutch save",
      },
    },
  }
  local view_targets = targets[view]
  if view_targets == nil then
    error("Unknown community view: " .. view)
  end
  local target = view_targets[device]
  if target == nil then
    error("Unknown community device: " .. device)
  end

  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript([=[
      location.pathname === "/"
        && document.body.innerText.includes("Sample Channel")
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]),
    "30s"
  )

  viset.page.navigate(base_url .. target.path .. "?simulationTheme=" .. theme)
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === %q
        && document.body.innerText.includes("Sample Channel")
        && document.body.innerText.includes(%q)
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]):format(target.path, target.ready)),
    "30s"
  )

  viset.sleep("350ms")
  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
