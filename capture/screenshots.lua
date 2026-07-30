--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media"
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
view = [
  "home",
  "channel-setup",
  "custom-commands",
  "guessing-leaderboard",
  "points-settings",
  "admin",
  "native-shoutouts",
  "native-polls",
  "native-clips-markers",
  "native-channel-points",
  "native-predictions",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_SCREENSHOT_PORT") or "43217"
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

local readiness = {
  ["home"] = {
    path = "/",
    expression = [[document.body.innerText.includes("Choose your chat tools")]],
  },
  ["channel-setup"] = {
    path = "/host",
    expression = [[document.body.innerText.includes("Chat tools")]],
  },
  ["custom-commands"] = {
    path = "/custom-commands/settings",
    expression = [[document.body.innerText.includes("Scheduled messages")]],
  },
  ["guessing-leaderboard"] = {
    path = "/guessing/leaderboard/samplechannel",
    expression = [[document.body.innerText.includes("Guessing leaderboard")]],
  },
  ["points-settings"] = {
    path = "/points/settings",
    expression = [[document.body.innerText.includes("Points settings")]],
  },
  ["admin"] = {
    path = "/admin",
    expression = [[document.body.innerText.includes("Channels using BlokeBot")]],
  },
  ["native-shoutouts"] = {
    path = "/twitch-operations/shoutouts",
    expression = [[Boolean(document.querySelector("[data-native-route='shoutouts'] .task-panel button"))]],
  },
  ["native-polls"] = {
    path = "/twitch-operations/polls",
    expression = [[Boolean(document.querySelector("[data-native-route='polls'] .task-panel button"))]],
  },
  ["native-clips-markers"] = {
    path = "/twitch-operations/clips-markers",
    expression = [[Boolean(document.querySelector("[data-native-route='clips-markers'] .task-panel button"))]],
  },
  ["native-channel-points"] = {
    path = "/twitch-operations/channel-points",
    expression = [[Boolean(document.querySelector("[data-native-route='channel-points'] [data-active-redemptions] [data-waiting-age-band]"))]],
  },
  ["native-predictions"] = {
    path = "/twitch-operations/predictions",
    expression = [[Boolean(document.querySelector("[data-native-route='predictions'] .task-panel button"))]],
  },
}

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local view = viset.context.axes.view
  local expected = assert(readiness[view], "No capture readiness is registered for " .. view)
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "20s" })
  viset.page.navigate(base_url .. "/simulation/login?view=" .. view .. "&theme=" .. theme)
  local ready_expression = ([=[
    window.location.pathname === %q &&
      document.body.innerText.includes("Sample Channel") &&
      (%s) &&
      getComputedStyle(document.querySelector("main")).opacity === "1"
  ]=]):format(expected.path, expected.expression)
  viset.page.wait_for(viset.javascript(ready_expression), "20s")
  if view == "native-shoutouts" then
    viset.sleep("350ms")
    viset.page.evaluate(viset.javascript([=[
      (() => {
        const trigger = [...document.querySelectorAll(".disclosure-trigger")].find(
          candidate => candidate.textContent.includes("Automatic raid shoutouts")
        );
        if (!trigger) throw new Error("Automatic raid shoutout disclosure was not found.");
        trigger.click();
        return true;
      })()
    ]=]))
    viset.page.wait_for(
      viset.javascript([[Boolean(document.querySelector("[data-automatic-raid-shoutouts]"))]]),
      "20s"
    )
  end
  viset.sleep("350ms")
  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
