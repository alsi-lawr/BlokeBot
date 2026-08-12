--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/community/v010"
output = "shoutout-setup-{theme}-{device}.webp"
frame = "builtin:auto"
frames_per_second = 30
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

[webp]
source = "png_screencast"
encoder = "libwebp_full"
pipeline = "live"
mode = "lossy"
quality = 100
method = 4

[devices.laptop]
mobile = false
touch = false
device_scale = 1.0

[devices.laptop.viewport]
width = 1180
height = 720

[matrix]
theme = ["light"]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_V010_SHOUTOUT_SCROLL_PORT") or "5474"
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

  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })

  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript([=[
      location.pathname === "/"
        && document.body.innerText.includes("Sample Channel")
        && document.querySelector("main") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]),
    "40s"
  )

  viset.page.navigate(base_url .. "/raid-collaboration?simulationTheme=" .. theme .. "#settings")
  viset.page.wait_for(
    viset.javascript([=[
      location.pathname === "/raid-collaboration"
        && document.querySelector("main") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
        && document.querySelector("[data-raid-settings]") !== null
        && document.querySelector("[data-automatic-raid-shoutouts]") !== null
    ]=]),
    "40s"
  )

  viset.page.evaluate(
    viset.javascript([=[
      (() => {
        window.scrollTo(0, 0);
        return true;
      })()
    ]=])
  )
  viset.sleep("400ms")

  local recording = viset.record()
  recording:start()
  recording:during("400ms")
  recording:during("4200ms", function()
    viset.page.animate({
      duration = "4200ms",
      easing = "in_out_sine",
      update = viset.javascript([=[
        frame => {
          const root = document.documentElement;
          const maximum = Math.max(0, root.scrollHeight - window.innerHeight);
          window.scrollTo(0, Math.round(maximum * frame.progress));
        }
      ]=]),
    })
  end)
  recording:during("400ms")
  recording:stop()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
