--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/community/v010"
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

[devices.phone]
mobile = true
touch = true
device_scale = 1.0

[devices.phone.viewport]
width = 390
height = 844

[matrix]
figure = [
  "raid-collaboration-light",
  "blokeraid-completion-dark",
  "collectives-recovery-dark",
  "viewer-passport-participant-dark",
  "moment-attachment-light",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_V010_FIGURES_PHONE_PORT") or "5473"
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
  local figure = viset.context.axes.figure
  local targets = {
    ["raid-collaboration-light"] = {
      path = "/raid-collaboration",
      theme = "light",
      features = "all-enabled",
      ready = "document.querySelector('[data-raid-history]') !== null",
      scroll = "[data-raid-shortlist] article",
    },
    ["blokeraid-completion-dark"] = {
      path = "/raid/samplechannel",
      theme = "dark",
      features = "all-enabled",
      ready = "document.querySelector('.public-raid-recap') !== null",
    },
    ["collectives-recovery-dark"] = {
      path = "/collectives",
      theme = "dark",
      features = "selective-native",
      ready = "document.querySelector('[data-collectives-disabled-recovery]') !== null",
    },
    ["viewer-passport-participant-dark"] = {
      path = "/passport/samplechannel/nightowl",
      theme = "dark",
      features = "all-enabled",
      ready = "!document.body.innerText.includes('Loading viewer passport')",
    },
    ["moment-attachment-light"] = {
      path = "/bounties/samplechannel",
      theme = "light",
      features = "all-enabled",
      ready = "document.querySelector('[data-public-moment-attachments]') !== null",
    },
  }

  local target = targets[figure]
  if target == nil then
    error("Unknown v0.10 guide figure: " .. figure)
  end

  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })

  viset.page.navigate(base_url .. "/simulation/login?view=home&theme=" .. target.theme)
  viset.page.wait_for(
    viset.javascript([=[
      location.pathname === "/"
        && document.body.innerText.includes("Sample Channel")
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]),
    "40s"
  )

  viset.page.evaluate(
    viset.javascript([=[
      ({ endpoint }) => fetch(endpoint, { method: "POST" }).then(response => {
        if (!response.ok) throw new Error(`${endpoint} returned ${response.status}`);
        return true;
      })
    ]=]),
    { endpoint = base_url .. "/simulation/commands/features/" .. target.features }
  )

  viset.page.navigate(base_url .. target.path .. "?simulationTheme=" .. target.theme)
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === %q
        && document.querySelector("main") !== null
        && getComputedStyle(document.querySelector("main")).opacity === "1"
        && (%s)
    ]=]):format(target.path, target.ready)),
    "40s"
  )
  if target.scroll ~= nil then
    viset.page.evaluate(
      viset.javascript([=[
        ({ selector }) => {
          const target = document.querySelector(selector);
          if (target) target.scrollIntoView({ block: "center" });
          return true;
        }
      ]=]),
      { selector = target.scroll }
    )
  end

  viset.sleep("350ms")
  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
