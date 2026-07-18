--[[
# viset
version = 1
output_root = "../.agent-workspace/viset-candidate"
output = "animations/{device}-{theme}-home-scroll.webp"
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
  local device = viset.context.device
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "20s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript [=[
      document.body.innerText.includes("Sample Channel") &&
        Boolean(document.querySelector("article"))
    ]=],
    "20s"
  )
  viset.sleep("350ms")

  viset.page.evaluate(
    viset.javascript [=[
      ({ touch }) => {
        window.scrollTo(0, 0);
        if (!touch) return true;
        const indicator = document.createElement("div");
        indicator.id = "blokebot-simulation-touch";
        indicator.setAttribute("aria-hidden", "true");
        indicator.style.cssText = [
          "position:fixed",
          "z-index:2147483647",
          "width:42px",
          "height:42px",
          "border:2px solid rgba(255,255,255,0.92)",
          "border-radius:999px",
          "background:rgba(148,163,184,0.22)",
          "box-shadow:0 0 0 2px rgba(15,23,42,0.68),0 4px 12px rgba(15,23,42,0.3)",
          "opacity:0",
          "pointer-events:none",
          "transform:translate(-50%,-50%) scale(0.9)",
        ].join(";");
        document.body.append(indicator);
        return true;
      }
    ]=],
    { touch = device.touch }
  )

  local function gesture(start_ratio, end_ratio)
    local update = string.format(
      [=[
        frame => {
          const root = document.documentElement;
          const maximum = Math.max(0, root.scrollHeight - window.innerHeight);
          const ratio = %s + (%s - %s) * frame.progress;
          window.scrollTo(0, Math.round(maximum * ratio));
          const indicator = document.querySelector("#blokebot-simulation-touch");
          if (indicator) {
            indicator.style.left = Math.round(window.innerWidth * 0.78) + "px";
            indicator.style.top = Math.round(
              window.innerHeight * (0.78 + (0.42 - 0.78) * frame.progress)
            ) + "px";
            indicator.style.opacity = "1";
          }
        }
      ]=],
      tostring(start_ratio),
      tostring(end_ratio),
      tostring(start_ratio)
    )
    viset.page.animate({
      duration = "700ms",
      easing = "in_out_sine",
      update = viset.javascript(update),
    })
    viset.page.evaluate(
      viset.javascript [=[
        document.querySelector("#blokebot-simulation-touch")?.style.setProperty("opacity", "0");
        true
      ]=]
    )
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
