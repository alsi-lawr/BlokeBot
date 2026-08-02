--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media"
output = "{device}-{theme}-overlay-{view}.png"
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
  "sources",
  "guessing",
  "giveaway",
  "event-feed",
  "viewer-queue",
  "cues",
  "media",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_V06_OVERLAY_GUIDES_PORT") or "5461"
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

local views = {
  ["sources"] = {
    path = "/overlays/sources",
    selected = "Channel event feed",
    scroll = "[data-overlay-tabs]",
    ready = "[data-overlay-editor]",
  },
  ["guessing"] = {
    path = "/overlays/sources",
    selected = "Guessing round",
    scroll = "[aria-labelledby='overlay-preview-title']",
    ready = "[data-draft-type='guessing']",
  },
  ["giveaway"] = {
    path = "/overlays/sources",
    selected = "Points giveaway",
    scroll = "[aria-labelledby='overlay-preview-title']",
    ready = "[data-draft-type='giveaway']",
  },
  ["event-feed"] = {
    path = "/overlays/sources",
    selected = "Channel event feed",
    scroll = "[data-overlay-editor]",
    ready = "[data-event-feed-kind-settings]",
  },
  ["viewer-queue"] = {
    path = "/overlays/sources",
    selected = "Viewer queue",
    scroll = "[aria-labelledby='overlay-preview-title']",
    ready = "[data-draft-type='viewerqueue']",
  },
  ["cues"] = {
    path = "/overlays/cues",
    scroll = "[data-card-owner='cue-workspace-columns']",
    ready = "[data-cue-editor]",
  },
  ["media"] = {
    path = "/overlays/media",
    scroll = "main",
    ready = "#media-name",
  },
}

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local view = viset.context.axes.view
  local expected = views[view]
  if expected == nil then
    error("No overlay capture state is registered for " .. view)
  end

  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?view=overlays&theme=" .. theme)
  viset.page.wait_for(
    viset.javascript([=[
      location.pathname === "/overlays/sources"
        && document.body.innerText.includes("Sample Channel")
        && document.querySelector("[data-overlay-editor]") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]),
    "30s"
  )
  viset.sleep("750ms")

  viset.page.evaluate(
    viset.javascript([=[
      async () => {
        const post = path => fetch(path, { method: "POST" }).then(response => {
          if (!response.ok) throw new Error(`${path} returned ${response.status}`);
        });
        await post("/simulation/commands/features/all-enabled");
        await post("/simulation/commands/round/open");
        await post("/simulation/commands/giveaway/active");
        await post("/simulation/commands/liveness/live");
        await new Promise(resolve => setTimeout(resolve, 350));
        return true;
      }
    ]=])
  )

  if expected.path ~= "/overlays/sources" then
    viset.page.navigate(base_url .. expected.path .. "?simulationTheme=" .. theme)
    viset.page.wait_for(
      viset.javascript(([=[
        location.pathname === %q
          && document.body.innerText.includes("Sample Channel")
          && getComputedStyle(document.querySelector("main")).opacity === "1"
      ]=]):format(expected.path)),
      "30s"
    )
    viset.sleep("750ms")
  end

  if expected.selected ~= nil then
    viset.page.evaluate(
      viset.javascript([=[
        async ({ selected }) => {
          const choice = [...document.querySelectorAll("[aria-label='Saved overlays'] button")]
            .find(candidate => candidate.textContent.includes(selected));
          if (!choice) throw new Error(`Saved Browser Source not found: ${selected}`);
          choice.click();
          await new Promise(resolve => setTimeout(resolve, 750));
          return true;
        }
      ]=]),
      { selected = expected.selected }
    )
  end

  viset.page.wait_for(
    viset.javascript(([[
      document.querySelector(%q) !== null
        && !document.body.innerText.includes("Loading overlays...")
        && !document.body.innerText.includes("Loading cues...")
        && !document.body.innerText.includes("Loading media...")
    ]]):format(expected.ready)),
    "30s"
  )

  if view == "guessing" then
    viset.page.wait_for(
      viset.javascript([=[
        document.querySelector("[data-draft-type='guessing'].overlay-preview-frame--ready")
          !== null
      ]=]),
      "30s"
    )
  elseif view == "giveaway" then
    viset.page.wait_for(
      viset.javascript([=[
        document.querySelector("[data-draft-type='giveaway'].overlay-preview-frame--ready")
          !== null
      ]=]),
      "30s"
    )
  elseif view == "event-feed" then
    viset.page.wait_for(
      viset.javascript([=[
        document.querySelector("[data-draft-type='eventfeed'].overlay-preview-frame--ready")
          !== null
      ]=]),
      "30s"
    )
  elseif view == "viewer-queue" then
    viset.page.wait_for(
      viset.javascript([=[
        document.querySelector("[data-draft-type='viewerqueue'].overlay-preview-frame--ready")
          !== null
      ]=]),
      "30s"
    )
  end

  viset.page.evaluate(
    viset.javascript([=[
      ({ selector }) => {
        const target = document.querySelector(selector);
        if (!target) throw new Error(`Capture scroll target not found: ${selector}`);
        target.scrollIntoView({ block: "start" });
        window.scrollBy(0, -12);
        return true;
      }
    ]=]),
    { selector = expected.scroll }
  )
  viset.sleep("500ms")
  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
