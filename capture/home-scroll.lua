--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/dashboard"
output = "{device}-{theme}-home-scroll.webp"
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
encoder = "libwebp_anim"
pipeline = "live"
mode = "lossy"
quality = 100
method = 4

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

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local device = viset.context.device
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript([=[
        Boolean(document.querySelector("article"))
          && document.querySelector("#components-reconnect-modal")?.open !== true
    ]=]),
    "20s"
  )

  viset.sleep("350ms")

  viset.page.evaluate(
    viset.javascript([=[
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
    ]=]),
    { touch = device.touch }
  )

  local function gesture(start_ratio, end_ratio)
    local update = viset
      .javascript([=[
      frame => {
        const root = document.documentElement;
        const maximum = Math.max(0, root.scrollHeight - window.innerHeight);
        const ratio = %.17g + %.17g * frame.progress;

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
    ]=])
      :format(start_ratio, end_ratio - start_ratio)

    viset.page.animate({
      duration = "700ms",
      easing = "in_out_sine",
      update = viset.javascript(update),
    })
    viset.page.evaluate(viset.javascript([=[
        document.querySelector("#blokebot-simulation-touch")?.style.setProperty("opacity", "0");
        true
      ]=]))
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

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
