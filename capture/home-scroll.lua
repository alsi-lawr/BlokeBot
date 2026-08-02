--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media"
output = "{device}-{theme}-home-scroll.webp"
frame = "builtin:auto"
frames_per_second = 50
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
source = "jpeg_screencast"
source_quality = 95
encoder = "libwebp_full"
pipeline = "live"
mode = "lossy"
quality = 75
method = 0

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
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_HOME_SCROLL_PORT") or "43218"
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
      document.body.innerText.includes("Sample Channel") &&
        Boolean(document.querySelector("article"))
    ]=]),
    "20s"
  )
  viset.sleep("350ms")

  viset.page.evaluate(viset.javascript("window.scrollTo(0, 0); true"))

  local function gesture(start_ratio, end_ratio)
    local update = viset
      .javascript([=[
      frame => {
        const root = document.documentElement;
        const maximum = Math.max(0, root.scrollHeight - window.innerHeight);
        const ratio = %.17g + %.17g * frame.progress;

        window.scrollTo(0, Math.round(maximum * ratio));

      }
    ]=])
      :format(start_ratio, end_ratio - start_ratio)

    viset.page.animate({
      duration = "700ms",
      easing = "in_out_sine",
      update = viset.javascript(update),
    })
  end

  local recording = viset.record()
  recording:start()
  recording:during("800ms")
  recording:during("700ms", function()
    gesture(0, 0.48)
  end)
  recording:during("267ms")
  recording:during("700ms", function()
    gesture(0.48, 1)
  end)
  recording:during("500ms")
  recording:stop()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
