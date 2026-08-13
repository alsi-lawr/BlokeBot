--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/community/progression"
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

[matrix]
theme = ["light", "dark"]
view = [
  "bingo-setup",
  "bingo-moderation",
  "bingo-archive",
  "bounties-setup",
  "bounties-moderation",
  "community-progression-setup",
  "community-progression-archive",
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
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "3s" })
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
    ]=]):format(path)),
    "40s"
  )
end

local targets = {
  ["bingo-setup"] = {
    path = "/bingo",
    features = "all-enabled",
    scroll = "[data-bingo-authoring]",
  },
  ["bingo-moderation"] = {
    path = "/bingo",
    features = "all-enabled",
    scroll = "[data-bingo-game]",
  },
  ["bingo-archive"] = {
    path = "/bingo",
    features = "all-enabled",
    scroll = "[data-bingo-revisions]",
  },
  ["bounties-setup"] = {
    path = "/bounties",
    features = "all-enabled",
    scroll = "[data-reward-distribution]",
  },
  ["bounties-moderation"] = {
    path = "/bounties",
    features = "all-enabled",
    scroll = "[data-bounty-id]",
  },
  ["community-progression-setup"] = {
    path = "/community",
    features = "all-enabled",
    scroll = "[data-season-activity]",
  },
  ["community-progression-archive"] = {
    path = "/community",
    features = "all-enabled",
    scroll = "[data-season-id]",
  },
}

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local view = viset.context.axes.view
  local target = targets[view]

  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  settle("/")

  viset.page.evaluate(
    viset.javascript([=[
      ({ features }) => fetch(`/simulation/commands/features/${features}`, { method: "POST" })
        .then(() => true)
    ]=]),
    { features = target.features }
  )

  viset.page.navigate(base_url .. target.path .. "?simulationTheme=" .. theme)
  settle(target.path)
  viset.sleep("750ms")

  if target.bottom then
    viset.page.evaluate("window.scrollTo(0, document.body.scrollHeight); true")
  elseif target.scroll ~= nil then
    viset.page.evaluate(
      viset.javascript([=[
        ({ selector }) => {
          document.querySelector(selector)?.scrollIntoView({ block: "start" });
          window.scrollBy(0, -12);
          return true;
        }
      ]=]),
      { selector = target.scroll }
    )
  end

  viset.sleep("600ms")
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
