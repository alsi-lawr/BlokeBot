--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media"
output = "{device}-{theme}-{view}.png"
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
  "viewer-command-catalog",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_VIEWER_COMMAND_CATALOG_PORT") or "5334"
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
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]),
    "30s"
  )

  viset.page.evaluate(
    viset.javascript([=[
      async () => {
        const post = path => fetch(path, { method: "POST" }).then(response => {
          if (!response.ok) throw new Error(`${path} returned ${response.status}`);
        });
        await post("/simulation/commands/round/open");
        await post("/simulation/commands/giveaway/active");
        await post("/simulation/commands/liveness/live");
        await post("/simulation/commands/features/all-enabled");
        await new Promise(resolve => setTimeout(resolve, 500));
        return true;
      }
    ]=])
  )

  local target = "/host"
  viset.page.navigate(base_url .. target .. "?simulationTheme=" .. theme)
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === %q
        && document.body.innerText.includes("Sample Channel")
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]):format(target)),
    "30s"
  )

  viset.page.wait_for(
    viset.javascript([[document.querySelector(".feature-toggle-card") !== null]]),
    "30s"
  )
  viset.page.evaluate(viset.javascript([=[
    (async () => {
      const stageHeader = title => [...document.querySelectorAll("button.studio-stage__header")]
        .find(candidate => candidate.querySelector(".studio-stage__title")?.textContent.trim()
          === title);
      const inventory = () => document.querySelector("[data-fold='command-inventory'] button");
      const commands = stageHeader("Commands");
      if (!commands || !inventory()) throw new Error("Commands disclosures were not found.");
      if (commands.getAttribute("aria-expanded") !== "true") {
        commands.click();
      }
      inventory().click();
      await new Promise(resolve => setTimeout(resolve, 1000));
      if (inventory()?.getAttribute("aria-expanded") !== "true") {
        inventory()?.click();
      }
      stageHeader("Commands").closest("section").scrollIntoView({ block: "start" });
      window.scrollBy(0, -12);
      return true;
    })()
  ]=]))
  viset.page.wait_for(
    viset.javascript([[document.querySelector("[data-command-catalog]") !== null]]),
    "30s"
  )
  viset.sleep("500ms")

  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
