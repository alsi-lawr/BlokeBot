--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media"
output = "{device}-{theme}-guessing-workflow.webp"
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
local port = os.getenv("BLOKEBOT_GUESSING_CAPTURE_PORT") or "43219"
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
  viset.page.navigate(base_url .. "/simulation/login?view=guessing&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript([=[
      document.body.innerText.includes("Sample Channel") &&
        document.body.innerText.includes("Run a round")
    ]=]),
    "20s"
  )
  viset.page.wait_for(
    viset.javascript([=[
      document.documentElement.scrollWidth <= window.innerWidth &&
        document.body.scrollWidth <= window.innerWidth
    ]=]),
    "10s"
  )
  viset.sleep("350ms")

  local click = viset.javascript([=[
    ({ label }) => {
      const button = [...document.querySelectorAll("button")]
        .find(candidate => candidate.textContent.trim() === label);
      if (!button) throw new Error(`Button not found: ${label}.`);
      button.click();
      return true;
    }
  ]=])

  local recording = viset.record()
  recording:start()
  recording:during("600ms")
  recording:during("233ms", function()
    viset.page.evaluate(click, { label = "History" })
  end)
  viset.page.wait_for("Boolean(document.querySelector('#guessing-history-panel'))", "10s")
  recording:during("333ms")
  recording:during("233ms", function()
    viset.page.evaluate(click, { label = "Leaderboard" })
  end)
  viset.page.wait_for("Boolean(document.querySelector('#guessing-leaderboard-panel'))", "10s")
  recording:during("267ms")
  recording:during("600ms", function()
    viset.page.evaluate(viset.javascript([=[
        window.blokeBotCaptureScroll = (() => {
          const target = document.querySelector("#guessing-leaderboard-panel");
          return {
            start: window.scrollY,
            end: target ? target.getBoundingClientRect().top + window.scrollY : window.scrollY,
          };
        })();
        true
      ]=]))
    viset.page.animate({
      duration = "600ms",
      easing = "in_out_sine",
      update = viset.javascript([=[
        frame => {
          const range = window.blokeBotCaptureScroll;
          window.scrollTo(0, Math.round(range.start + (range.end - range.start) * frame.progress));
        }
      ]=]),
    })
  end)
  viset.page.evaluate(
    viset.javascript([=[
      ({ value }) => {
        const input = document.querySelector("#leaderboardUsername");
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
        setter.call(input, value);
        input.dispatchEvent(new Event("input", { bubbles: true }));
        return true;
      }
    ]=]),
    { value = "nightowl" }
  )
  recording:during("467ms")
  recording:during("233ms", function()
    viset.page.evaluate(click, { label = "Live" })
  end)
  viset.page.wait_for("document.body.innerText.includes('Run a round')", "10s")
  viset.page.evaluate("window.scrollTo(0, 0); true")
  recording:during("600ms")
  recording:stop()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
