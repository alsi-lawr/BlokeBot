--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/community/figures"
output = "{device}-{theme}-{figure}.png"
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
figure = [
  "competition-result",
  "progression-overlay-setup",
  "achievement-feed-setup",
  "shoutout-setup",
  "raid-collaboration",
  "blokeraid-completion",
  "collectives-recovery",
  "viewer-passport-participant",
  "moment-attachment",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_CAPTURE_PORT") or "5473"
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

local targets = {
  ["competition-result"] = {
    path = "/competitions",
    fragment = "#standings",
    features = "all-enabled",
  },
  ["progression-overlay-setup"] = {
    path = "/overlays",
    fragment = "#sources",
    features = "all-enabled",
    selected = "Community milestone",
  },
  ["achievement-feed-setup"] = {
    path = "/overlays",
    fragment = "#sources",
    features = "all-enabled",
    selected = "Channel event feed",
  },
  ["shoutout-setup"] = {
    path = "/raid-collaboration",
    fragment = "#settings",
    features = "all-enabled",
    scroll = "[data-automatic-raid-shoutouts]",
  },
  ["raid-collaboration"] = {
    path = "/raid-collaboration",
    features = "all-enabled",
    scroll = "[data-raid-shortlist] article",
  },
  ["blokeraid-completion"] = {
    path = "/raid/samplechannel",
    features = "all-enabled",
  },
  ["collectives-recovery"] = {
    path = "/collectives",
    features = "selective-native",
  },
  ["viewer-passport-participant"] = {
    path = "/passport/samplechannel/nightowl",
    features = "all-enabled",
  },
  ["moment-attachment"] = {
    path = "/bounties/samplechannel",
    features = "all-enabled",
  },
}

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local figure = viset.context.axes.figure
  local target = targets[figure]

  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  settle("/")

  viset.page.evaluate(
    viset.javascript([=[
      (async ({ features }) => {
        const post = path => fetch(path, { method: "POST" });
        await post(`/simulation/commands/features/${features}`);
        await post("/simulation/commands/liveness/production");
        await new Promise(resolve => setTimeout(resolve, 400));
        return true;
      })
    ]=]),
    { features = target.features }
  )
  if target.features ~= "all-disabled" then
    viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  else
    viset.sleep("2s")
  end

  local fragment = target.fragment or ""
  viset.page.navigate(base_url .. target.path .. "?simulationTheme=" .. theme .. fragment)
  settle(target.path)
  viset.sleep("750ms")

  if target.selected ~= nil then
    viset.page.evaluate(
      viset.javascript([=[
        (async ({ selected }) => {
          [...document.querySelectorAll("[aria-label='Saved overlays'] button")]
            .find(candidate => candidate.textContent.includes(selected))
            ?.click();
          await new Promise(resolve => setTimeout(resolve, 750));
          return true;
        })
      ]=]),
      { selected = target.selected }
    )
  end

  if target.scroll ~= nil then
    viset.page.evaluate(
      viset.javascript([=[
        ({ selector }) => {
          document.querySelector(selector)?.scrollIntoView({ block: "center" });
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
