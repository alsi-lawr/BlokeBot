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
  "overlays",
  "viewer-command-catalog",
  "chat-tools-all-disabled",
  "chat-tools-enabled",
]
]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_V05_GUIDES_PORT") or "5334"
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
  local device = viset.context.device.name
  local feature_state = ({
    ["overlays"] = "all-enabled",
    ["viewer-command-catalog"] = "all-enabled",
    ["chat-tools-all-disabled"] = "all-disabled",
    ["chat-tools-enabled"] = "mixed",
  })[view]
  assert(feature_state ~= nil, "No deterministic feature state is registered for " .. view)

  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "60s" })
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
      async ({ featureState, catalog }) => {
        const post = path => fetch(path, { method: "POST" }).then(response => {
          if (!response.ok) throw new Error(`${path} returned ${response.status}`);
        });
        await post(catalog
          ? "/simulation/commands/round/open"
          : "/simulation/commands/round/none");
        await post(catalog
          ? "/simulation/commands/giveaway/active"
          : "/simulation/commands/giveaway/inactive");
        await post(catalog || featureState === "mixed"
          ? "/simulation/commands/liveness/live"
          : "/simulation/commands/liveness/offline");
        await post(`/simulation/commands/features/${featureState}`);
        await new Promise(resolve => setTimeout(resolve, 500));
        return true;
      }
    ]=]),
    {
      featureState = feature_state,
      catalog = view == "viewer-command-catalog",
    }
  )

  local target = view == "overlays" and "/overlays" or "/host"
  viset.page.navigate(base_url .. target .. "?simulationTheme=" .. theme)
  viset.page.wait_for(
    viset.javascript(([=[
      location.pathname === %q
        && document.body.innerText.includes("Sample Channel")
        && getComputedStyle(document.querySelector("main")).opacity === "1"
    ]=]):format(target)),
    "30s"
  )

  if view == "overlays" then
    viset.page.wait_for(
      viset.javascript([=[
        document.body.innerText.includes("Transparent browser source")
          && document.querySelector("[data-overlay-editor]") !== null
          && document.querySelector(".overlay-preview-frame") !== null
      ]=]),
      "30s"
    )
    viset.sleep("500ms")
    local safety = viset.page.evaluate(viset.javascript([=[
      (() => ({
        privateUrlVisible: document.querySelector("[data-private-url-reveal]") !== null,
        seededAccessKeyVisible: document.documentElement.innerHTML.includes(
          "simulation-overlay-access-key-0000000000000"
        ),
        horizontalOverflow: document.documentElement.scrollWidth > innerWidth,
        cardClearance: getComputedStyle(document.documentElement)
          .getPropertyValue("--app-card-clearance").trim(),
      }))()
    ]=]))
    assert(safety.privateUrlVisible == false, "A private overlay URL reveal is visible")
    assert(safety.seededAccessKeyVisible == false, "The seeded private access key is visible")
    assert(safety.horizontalOverflow == false, "The Overlays dashboard has horizontal overflow")
    assert(safety.cardClearance == "12px", "The shared card clearance is not 12px")
  elseif view == "viewer-command-catalog" then
    viset.page.wait_for(
      viset.javascript([[document.querySelectorAll(".feature-toggle-card").length === 12]]),
      "30s"
    )
    viset.page.evaluate(viset.javascript([=[
      (async () => {
        const disclosure = title => [...document.querySelectorAll("button.disclosure-trigger")]
          .find(candidate => candidate.querySelector(".disclosure-title")?.textContent.trim()
            === title);
        let commands = disclosure("Commands");
        let available = disclosure("Available viewer commands");
        if (!available || !commands) throw new Error("Commands disclosures were not found.");
        available.click();
        await new Promise(resolve => setTimeout(resolve, 1000));
        available = disclosure("Available viewer commands");
        if (available?.getAttribute("aria-expanded") !== "true") {
          available?.click();
        }
        commands = disclosure("Commands");
        commands.closest("section").scrollIntoView({ block: "start" });
        window.scrollBy(0, -12);
        return true;
      })()
    ]=]))
    viset.page.wait_for(
      viset.javascript([=[
        document.querySelector("[data-command-catalog]")?.textContent.includes("!commands")
          && document.querySelector("[data-command-catalog]")?.textContent.includes("!guess")
          && document.querySelector("[data-command-catalog]")?.textContent.includes("!enter")
          && document.querySelector("[data-command-catalog]")?.textContent.includes("!moment")
      ]=]),
      "30s"
    )
    viset.sleep("500ms")
    local catalog = viset.page.evaluate(viset.javascript([=[
      (() => {
        const text = document.querySelector("[data-command-catalog]")?.textContent ?? "";
        return {
          horizontalOverflow: document.documentElement.scrollWidth > innerWidth,
          count: document.querySelectorAll("[data-command-catalog] li").length,
          hasSecondaryAlias: text.includes("!hello"),
          hasModeratorCommand: text.includes("!modfixture"),
          conflictVisible: document.querySelector("[data-command-catalog-conflicts]") !== null,
          triggerValue: document.querySelector("#commands-aliases")?.value ?? "",
          cardClearance: getComputedStyle(document.documentElement)
            .getPropertyValue("--app-card-clearance").trim(),
        };
      })()
    ]=]))
    assert(catalog.horizontalOverflow == false, "The command catalog has horizontal overflow")
    assert(catalog.count >= 45, "The deterministic long command catalog is incomplete")
    assert(catalog.hasSecondaryAlias == false, "A secondary Custom Command alias is visible")
    assert(catalog.hasModeratorCommand == false, "A moderator-only command is visible")
    assert(catalog.conflictVisible == true, "The deterministic command conflict is not visible")
    assert(catalog.triggerValue == "commands", "The global Commands trigger is not deterministic")
    assert(catalog.cardClearance == "12px", "The shared card clearance is not 12px")
  else
    viset.page.wait_for(
      viset.javascript([[document.querySelectorAll(".feature-toggle-card").length === 12]]),
      "30s"
    )
    viset.page.evaluate(viset.javascript([=[
      (() => {
        const target = document.querySelector("#chat-tools");
        if (!target) throw new Error("Chat tools section is missing.");
        target.scrollIntoView({ block: "start" });
        window.scrollBy(0, -12);
        return true;
      })()
    ]=]))
    viset.sleep("350ms")
    local setup = viset.page.evaluate(
      viset.javascript([=[
        ({ featureState }) => {
          const names = [...document.querySelectorAll(".feature-toggle-card")]
            .map(button => button.querySelector("span.truncate")?.textContent.trim() ?? "");
          const enabled = [...document.querySelectorAll(".feature-toggle-card")]
            .filter(button => button.hasAttribute("aria-pressed"))
            .map(button => button.querySelector("span.truncate")?.textContent.trim() ?? "");
          const expectedNames = [
            "Shoutouts",
            "Polls",
            "Clips & markers",
            "Rewards & redemptions",
            "Predictions",
            "Request boards",
            "Play with viewers",
            "Moments",
            "Overlays",
            "Guessing game",
            "Points",
            "Custom commands",
          ];
          const expectedEnabled = featureState === "all-disabled"
            ? []
            : ["Request boards", "Moments", "Points", "Custom commands"];
          const owner = document.querySelector(
            "[data-card-owner='channel-setup-feature-cards']"
          );
          const style = owner ? getComputedStyle(owner) : null;
          const directCards = owner ? [...owner.children] : [];
          const distances = directCards.flatMap((first, firstIndex) => {
            const firstBox = first.getBoundingClientRect();
            return directCards.slice(firstIndex + 1).flatMap(second => {
              const secondBox = second.getBoundingClientRect();
              const horizontalOverlap =
                Math.min(firstBox.right, secondBox.right)
                - Math.max(firstBox.left, secondBox.left);
              const verticalOverlap =
                Math.min(firstBox.bottom, secondBox.bottom)
                - Math.max(firstBox.top, secondBox.top);
              if (horizontalOverlap > 0) {
                return [Math.max(firstBox.top, secondBox.top)
                  - Math.min(firstBox.bottom, secondBox.bottom)];
              }
              if (verticalOverlap > 0) {
                return [Math.max(firstBox.left, secondBox.left)
                  - Math.min(firstBox.right, secondBox.right)];
              }
              return [];
            });
          }).filter(distance => distance >= 0);
          const nav = document.querySelector("#desktop-navigation-rail");
          const navText = nav?.textContent ?? "";
          const normalizedNavText = navText.toLowerCase();
          return {
            names,
            enabled,
            expectedNames,
            expectedEnabled,
            ownerPresent: owner !== null,
            rowGap: style?.rowGap ?? "",
            columnGap: style?.columnGap ?? "",
            nearestCardDistance: distances.length > 0 ? Math.min(...distances) : -1,
            horizontalOverflow: document.documentElement.scrollWidth > innerWidth,
            cardClearance: getComputedStyle(document.documentElement)
              .getPropertyValue("--app-card-clearance").trim(),
            desktopNavHasChatTools: normalizedNavText.includes("chat tools"),
            desktopNavHasRequests: navText.includes("Request boards"),
            desktopNavHasMoments: navText.includes("Moments"),
            desktopNavHasPoints: navText.includes("Points"),
            desktopNavHasCustomCommands: navText.includes("Custom commands"),
          };
        }
      ]=]),
      { featureState = feature_state }
    )
    assert(
      table.concat(setup.names, "|") == table.concat(setup.expectedNames, "|"),
      "The twelve Chat Tools feature cards are incomplete or out of order"
    )
    assert(
      table.concat(setup.enabled, "|") == table.concat(setup.expectedEnabled, "|"),
      "The deterministic enabled feature set is wrong"
    )
    assert(setup.ownerPresent == true, "The semantic feature-card collection owner is missing")
    assert(setup.rowGap == "12px", "The semantic feature-card row gap is not 12px")
    assert(setup.columnGap == "12px", "The semantic feature-card column gap is not 12px")
    assert(setup.nearestCardDistance == 12, "The nearest feature-card clearance is not 12px")
    assert(setup.cardClearance == "12px", "The shared card clearance token is not 12px")
    assert(setup.horizontalOverflow == false, "Channel setup has horizontal overflow")
    if device == "laptop" then
      if feature_state == "all-disabled" then
        assert(
          setup.desktopNavHasChatTools == false,
          "Chat Tools navigation remains when every feature is disabled"
        )
      else
        assert(setup.desktopNavHasChatTools == true, "Enabled Chat Tools navigation is missing")
        assert(setup.desktopNavHasRequests == true, "Request boards navigation is missing")
        assert(setup.desktopNavHasMoments == true, "Moments navigation is missing")
        assert(setup.desktopNavHasPoints == true, "Points navigation is missing")
        assert(
          setup.desktopNavHasCustomCommands == true,
          "Custom commands navigation is missing"
        )
      end
    end
  end

  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
