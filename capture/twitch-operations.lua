--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media"
output = "{device}-{theme}-twitch-{feature}.png"
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
feature = ["shoutouts", "polls", "clips-markers", "channel-points", "predictions"]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_TWITCH_OPERATIONS_CAPTURE_PORT") or "43220"
local base_url = "http://127.0.0.1:" .. port
local headings = {
  ["shoutouts"] = "Shoutouts",
  ["polls"] = "Polls",
  ["clips-markers"] = "Clips & Markers",
  ["channel-points"] = "Rewards & Redemptions",
  ["predictions"] = "Twitch Channel Points Predictions",
}
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
  local feature = viset.context.axes.feature
  local heading = headings[feature]
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "20s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  viset.page.wait_for("document.body.innerText.includes('Sample Channel')", "20s")
  viset.page.navigate(base_url .. "/twitch-operations?simulationTheme=" .. theme)
  viset.page.wait_for(
    "[...document.querySelectorAll('h2')].some(candidate => candidate.textContent.trim() === 'Twitch Channel Points Predictions')",
    "20s"
  )
  viset.sleep("600ms")
  viset.page.evaluate(
    viset.javascript([=[
      ({ heading }) => {
        const target = [...document.querySelectorAll("h2")]
          .find(candidate => candidate.textContent.trim() === heading);
        if (!target) throw new Error(`Heading not found: ${heading}.`);
        const targetSection = target.closest("section");
        for (const section of targetSection.parentElement.querySelectorAll(":scope > section")) {
          section.hidden = section !== targetSection;
        }
        window.scrollTo(0, 0);
        return true;
      }
    ]=]),
    { heading = heading }
  )
  viset.sleep("250ms")
  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
