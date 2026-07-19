--[[
# viset
version = 1
output_root = "../assets/simulation"
output = "animations/{device}-{theme}-points-workflow.webp"
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
local port = os.getenv("BLOKEBOT_POINTS_CAPTURE_PORT") or "43220"
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
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "20s" })
  viset.page.navigate(base_url .. "/simulation/login?view=points&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript([=[
      document.body.innerText.includes("Sample Channel") &&
        document.body.innerText.includes("Viewer points")
    ]=]),
    "20s"
  )
  viset.sleep("350ms")
  viset.page.evaluate(viset.javascript([=[
      (() => {
        const heading = [...document.querySelectorAll("h2")]
          .find(candidate => candidate.textContent.trim() === "Viewer points");
        const section = heading?.closest("section");
        const topbar = document.querySelector(".app-shell__topbar")?.getBoundingClientRect().height ?? 0;
        const header = document.querySelector(".page-header")?.getBoundingClientRect().height ?? 0;
        if (section) {
          const top = section.getBoundingClientRect().top + window.scrollY;
          window.scrollTo(0, Math.max(0, top - topbar - header - 16));
        }
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
      })()
    ]=]))

  local touch = viset.javascript([=[
    ({ selector, label, visible }) => {
      const indicator = document.querySelector("#blokebot-simulation-touch");
      const target = label
        ? [...document.querySelectorAll("button")]
            .find(candidate => candidate.textContent.trim() === label)
        : document.querySelector(selector);
      if (!indicator || !target) return false;
      const bounds = target.getBoundingClientRect();
      indicator.style.left = Math.round(bounds.left + bounds.width / 2) + "px";
      indicator.style.top = Math.round(bounds.top + bounds.height / 2) + "px";
      indicator.style.opacity = visible ? "1" : "0";
      return true;
    }
  ]=])
  local set_value = viset.javascript([=[
    ({ value }) => {
      const input = document.querySelector("#points-dashboard-lookup-login");
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
      setter.call(input, value);
      input.dispatchEvent(new Event("input", { bubbles: true }));
      return true;
    }
  ]=])

  local recording = viset.record()
  recording:start()
  recording:during("600ms")
  viset.page.evaluate(touch, { selector = "#points-dashboard-lookup-login", visible = true })
  recording:during("100ms")
  viset.page.evaluate(touch, { selector = "#points-dashboard-lookup-login", visible = false })
  viset.page.evaluate(set_value, { value = "n" })
  recording:during("133ms")
  viset.page.evaluate(set_value, { value = "night" })
  recording:during("133ms")
  viset.page.evaluate(set_value, { value = "nightowl" })
  recording:during("267ms")
  viset.page.evaluate(touch, { label = "Search", visible = true })
  recording:during("100ms")
  viset.page.evaluate(viset.javascript([=[
      (() => {
        const button = [...document.querySelectorAll("button")]
          .find(candidate => candidate.textContent.trim() === "Search");
        if (!button) throw new Error("Search button not found.");
        button.click();
        document.querySelector("#blokebot-simulation-touch")?.style.setProperty("opacity", "0");
        return true;
      })()
    ]=]))
  viset.page.wait_for("document.body.innerText.includes('1,840 points')", "10s")
  recording:during("800ms")
  recording:stop()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
