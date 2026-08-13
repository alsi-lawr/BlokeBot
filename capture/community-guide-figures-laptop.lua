--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/community/figures"
output = "{figure}-{device}.png"
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

[matrix]
figure = [
  "competition-result-light",
  "progression-overlay-setup-light",
  "achievement-feed-setup-dark",
  "shoutout-setup-light",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_COMMUNITY_FIGURES_LAPTOP_PORT") or "5475"
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

local succeeded, failure = pcall(function()
  local figure = viset.context.axes.figure
  local targets = {
    ["competition-result-light"] = {
      path = "/competitions",
      fragment = "#standings",
      theme = "light",
    },
    ["progression-overlay-setup-light"] = {
      path = "/overlays",
      fragment = "#sources",
      theme = "light",
      selected = "Community milestone",
    },
    ["shoutout-setup-light"] = {
      path = "/raid-collaboration",
      fragment = "#settings",
      theme = "light",
      scroll = "[data-automatic-raid-shoutouts]",
    },
    ["achievement-feed-setup-dark"] = {
      path = "/overlays",
      fragment = "#sources",
      theme = "dark",
      selected = "Channel event feed",
    },
  }

  local target = targets[figure]

  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "90s" })

  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. target.theme)
  viset.page.wait_for(
    viset.javascript([=[
      location.pathname === "/"
        && document.querySelector("main") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]),
    "40s"
  )

  viset.page.evaluate(
    viset.javascript([=[
      (async () => {
        const post = path => fetch(path, { method: "POST" });
        await post("/simulation/commands/features/all-enabled");
        await post("/simulation/commands/liveness/production");
        await new Promise(resolve => setTimeout(resolve, 400));
        return true;
      })()
    ]=])
  )

  viset.page.navigate(
    base_url .. target.path .. "?simulationTheme=" .. target.theme .. target.fragment
  )
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === %q
        && document.querySelector("main") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]):format(target.path)),
    "40s"
  )

  if target.selected ~= nil then
    viset.page.evaluate(
      viset.javascript([=[
        async ({ selected }) => {
          const choice = [...document.querySelectorAll("[aria-label='Saved overlays'] button")]
            .find(candidate => candidate.textContent.includes(selected));
          choice?.click();
          await new Promise(resolve => setTimeout(resolve, 750));
          return true;
        })()
      ]=]),
      { selected = target.selected }
    )
  end

  if target.scroll ~= nil then
    viset.page.evaluate(
      viset.javascript([=[
        ({ selector }) => {
          const target = document.querySelector(selector);
          if (target) target.scrollIntoView({ block: "center" });
          return true;
        })()
      ]=]),
      { selector = target.scroll }
    )
  end

  viset.sleep("400ms")
  viset.snapshot()
end)

if server ~= nil then
  viset.process.stop(server)
end
if not succeeded then
  error(failure, 0)
end
