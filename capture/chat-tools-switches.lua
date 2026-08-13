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
  "chat-tools-all-disabled",
  "chat-tools-enabled",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_CHAT_TOOLS_PORT") or "5335"
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
  local view = viset.context.axes.view
  local feature_state = ({
    ["chat-tools-all-disabled"] = "all-disabled",
    ["chat-tools-enabled"] = "mixed",
  })[view]
  if feature_state == nil then
    error("No deterministic feature state is registered for " .. view)
  end

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
      async ({ featureState }) => {
        const post = path => fetch(path, { method: "POST" }).then(response => {
          if (!response.ok) throw new Error(`${path} returned ${response.status}`);
        });
        await post("/simulation/commands/round/none");
        await post("/simulation/commands/giveaway/inactive");
        await post(featureState === "mixed"
          ? "/simulation/commands/liveness/live"
          : "/simulation/commands/liveness/offline");
        await post(`/simulation/commands/features/${featureState}`);
        await new Promise(resolve => setTimeout(resolve, 500));
        return true;
      }
    ]=]),
    { featureState = feature_state }
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
    viset.javascript([[
      document.querySelector("#chat-tools") !== null
        && document.querySelectorAll(".feature-toggle-card").length > 0
    ]]),
    "30s"
  )

  viset.page.evaluate(
    viset.javascript([=[
      (async ({ featureState }) => {
        const expectedEnabled = new Set(featureState === "mixed"
          ? ["Request boards", "Moments", "Points", "Custom commands"]
          : []);
        const hasRequestedState = () => {
          const cards = [...document.querySelectorAll(".feature-toggle-card")];
          return cards.length > 0
            && cards.every(card => {
              const name = card.querySelector(".truncate")?.textContent.trim();
              return name
                && card.hasAttribute("aria-pressed") === expectedEnabled.has(name);
            });
        };
        const isInView = target => {
          const bounds = target.getBoundingClientRect();
          return bounds.top >= 0 && bounds.top < window.innerHeight
            && bounds.bottom > 0;
        };
        const deadline = performance.now() + 30000;
        let stableSince = null;
        while (performance.now() < deadline) {
          const target = document.querySelector("#chat-tools");
          if (!target) throw new Error("Chat tools section is missing.");
          if (!hasRequestedState() || !isInView(target)) {
            stableSince = null;
            target.scrollIntoView({ block: "start" });
            window.scrollBy(0, -12);
          } else {
            stableSince ??= performance.now();
            if (performance.now() - stableSince >= 500) return true;
          }
          await new Promise(resolve => setTimeout(resolve, 50));
        }
        throw new Error(`Chat tools did not remain stable for ${featureState}.`);
      })
    ]=]),
    { featureState = feature_state }
  )
  viset.page.wait_for(
    viset.javascript(([=[
      (() => {
        const expectedEnabled = new Set(%s === "mixed"
          ? ["Request boards", "Moments", "Points", "Custom commands"]
          : []);
        const target = document.querySelector("#chat-tools");
        const cards = [...document.querySelectorAll(".feature-toggle-card")];
        if (!target || cards.length === 0) return false;
        const bounds = target.getBoundingClientRect();
        return bounds.top >= 0 && bounds.top < window.innerHeight
          && bounds.bottom > 0
          && cards.every(card => {
            const name = card.querySelector(".truncate")?.textContent.trim();
            return name
              && card.hasAttribute("aria-pressed") === expectedEnabled.has(name);
          });
      })()
    ]=]):format(string.format("%q", feature_state))),
    "30s"
  )

  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
