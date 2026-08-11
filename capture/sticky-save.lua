--[[
# viset
version = 1
output_root = "output/sticky-save"
output = "{device}.png"
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

[devices.desktop-light-long-validation-focus]
mobile = false
touch = false
device_scale = 1.0

[devices.desktop-light-long-validation-focus.viewport]
width = 1440
height = 900

[devices.mobile-dark-repeated-editor]
mobile = true
touch = true
device_scale = 1.0

[devices.mobile-dark-repeated-editor.viewport]
width = 390
height = 844

[devices.mobile-light-dynamic-create]
mobile = true
touch = true
device_scale = 1.0

[devices.mobile-light-dynamic-create.viewport]
width = 390
height = 844

[devices.desktop-dark-dynamic-save]
mobile = false
touch = false
device_scale = 1.0

[devices.desktop-dark-dynamic-save.viewport]
width = 1440
height = 900

[devices.mobile-dark-modal-save]
mobile = true
touch = true
device_scale = 1.0

[devices.mobile-dark-modal-save.viewport]
width = 390
height = 844

[devices.desktop-light-disabled-save]
mobile = false
touch = false
device_scale = 1.0

[devices.desktop-light-disabled-save.viewport]
width = 1440
height = 900

]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_STICKY_SAVE_PORT") or "43232"
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

local captures = {
  ["desktop-light-long-validation-focus"] = {
    theme = "light",
    view = "long-validation-focus",
    login = "points-settings",
  },
  ["mobile-dark-repeated-editor"] = {
    theme = "dark",
    view = "repeated-editor",
    login = "home",
  },
  ["mobile-light-dynamic-create"] = {
    theme = "light",
    view = "dynamic-create",
    login = "native-channel-points",
  },
  ["desktop-dark-dynamic-save"] = {
    theme = "dark",
    view = "dynamic-save",
    login = "native-channel-points",
  },
  ["mobile-dark-modal-save"] = {
    theme = "dark",
    view = "modal-save",
    login = "guessing-settings",
  },
  ["desktop-light-disabled-save"] = {
    theme = "light",
    view = "disabled-save",
    login = "custom-commands",
  },
}

local function wait_for_text(text)
  viset.page.wait_for(
    viset.javascript(([[document.body.innerText.includes(%q)]]):format(text)),
    "20s"
  )
end

local function click_text(selector, text)
  viset.page.evaluate(
    viset.javascript(([=[
      (() => {
        const candidate = [...document.querySelectorAll(%q)].find(element =>
          element.textContent.includes(%q)
        );
        if (!candidate) throw new Error(`Control was not found: ${%q}`);
        candidate.click();
        return true;
      })()
    ]=]):format(selector, text, text))
  )
end

local function assert_architecture()
  viset.page.evaluate(viset.javascript([=[
    (() => {
      for (const element of document.querySelectorAll(".page-header__actions, .studio__header")) {
        const position = getComputedStyle(element).position;
        if (position === "sticky" || position === "fixed") {
          throw new Error(`Generic action container is ${position}`);
        }
      }
      if (document.documentElement.scrollWidth > document.documentElement.clientWidth + 1) {
        throw new Error(
          `Horizontal overflow: ${document.documentElement.scrollWidth}/${document.documentElement.clientWidth}`
        );
      }
      return true;
    })()
  ]=]))
end

local function assert_active_geometry(minimum_target)
  viset.page.evaluate(
    viset.javascript(([=[
      (() => {
        const regions = [...document.querySelectorAll(
          ".sticky-save-region[data-save-active='true'][data-save-visible='true']"
        )];
        if (regions.length === 0) throw new Error("No active visible Save region");
        const region = regions.at(-1);
        const surface = region.querySelector(".sticky-save-region__surface");
        const button = region.querySelector("button, .btn-primary, .btn-secondary");
        const surfaceRect = surface.getBoundingClientRect();
        const buttonRect = button.getBoundingClientRect();
        const modal = region.dataset.saveScope === "modal";
        if (modal && !region.closest("[role='dialog']")) {
          throw new Error("Modal Save escaped its dialog");
        }
        if (!modal && getComputedStyle(surface).position !== "fixed") {
          throw new Error("The active Save surface is not fixed");
        }
        if (surfaceRect.left < -0.5 || surfaceRect.right > innerWidth + 0.5) {
          throw new Error(`Save surface exceeds viewport: ${surfaceRect.left}/${surfaceRect.right}`);
        }
        if (
          !modal &&
          (surfaceRect.bottom > innerHeight + 0.5 || surfaceRect.bottom < innerHeight - 40)
        ) {
          throw new Error(`Save surface is outside its bottom inset: ${surfaceRect.bottom}/${innerHeight}`);
        }
        if (buttonRect.height + 0.5 < %d) {
          throw new Error(
            `Save target is too short: ${buttonRect.height}; min-height ${getComputedStyle(button).minHeight}`
          );
        }
        if (document.documentElement.scrollWidth > document.documentElement.clientWidth + 1) {
          throw new Error(
            `Horizontal overflow: ${document.documentElement.scrollWidth}/${document.documentElement.clientWidth}`
          );
        }
        return true;
      })()
    ]=]):format(minimum_target))
  )
end

local function prepare_long_validation()
  click_text(".studio-stage__header", "Gambling")
  viset.page.wait_for("Boolean(document.querySelector('#gamblingCooldown'))", "10s")
  viset.page.evaluate(viset.javascript([=[
    (() => {
      const input = document.querySelector("#gamblingCooldown");
      input.value = "-1";
      input.dispatchEvent(new Event("input", { bubbles: true }));
      document.querySelector(".sticky-save-region button").click();
      return true;
    })()
  ]=]))
  viset.page.wait_for("document.activeElement?.id === 'gamblingCooldown'", "10s")
  viset.page.evaluate(viset.javascript([=[
    (() => {
      const input = document.querySelector("#gamblingCooldown");
      input.scrollIntoView({ block: "end" });
      window.scrollBy(0, 80);
      return true;
    })()
  ]=]))
end

local function prepare_repeated(theme)
  viset.page.navigate(base_url .. "/queues?simulationTheme=" .. theme)
  wait_for_text("Play with viewers")
  viset.page.wait_for("Boolean(document.querySelector('.page-header__actions a'))", "10s")
  viset.page.evaluate(viset.javascript([=[
    (() => {
      window.scrollTo(0, 0);
      const ordinary = document.querySelector(".page-header__actions a");
      const before = ordinary.getBoundingClientRect().top;
      window.scrollBy(0, 240);
      const after = ordinary.getBoundingClientRect().top;
      if (before - after < 180) {
        throw new Error(`Ordinary action did not scroll normally: ${before}/${after}`);
      }
      window.scrollTo(0, 0);
      return true;
    })()
  ]=]))
  click_text("button", "Run the queue")
  viset.page.wait_for("document.querySelectorAll('[data-waiting-row]').length >= 2", "20s")
  local open_two = viset.javascript([=[
    (() => {
      const folds = [...document.querySelectorAll("[data-waiting-row] button")].filter(button =>
        button.textContent.includes("Priority & private note")
      );
      if (folds.length < 2) throw new Error("Two repeated editors were not found");
      folds[0].click();
      folds[1].click();
      return true;
    })()
  ]=])
  viset.page.evaluate(open_two)
  viset.page.wait_for(
    "document.querySelectorAll(\".sticky-save-region[data-save-active='true']\").length === 2",
    "10s"
  )
  viset.page.evaluate(viset.javascript([=[
    (() => {
      const activeEditors = [...document.querySelectorAll("[data-waiting-row]")].filter(row =>
        row.querySelector(".sticky-save-region[data-save-active='true']")
      );
      if (activeEditors.length !== 1) {
        throw new Error(`Expected one active repeated editor, got ${activeEditors.length}`);
      }
      activeEditors[0].scrollIntoView({ block: "center" });
      return true;
    })()
  ]=]))
end

local function prepare_dynamic_save()
  click_text(".studio-stage__header", "Rewards")
  viset.page.wait_for("Boolean(document.querySelector('[data-channel-point-rewards]'))", "10s")
  click_text("[data-channel-point-rewards] button", "Edit")
  viset.page.wait_for(
    "Boolean(document.querySelector(\".sticky-save-region[data-save-active='true']\"))",
    "10s"
  )
  viset.page.evaluate(viset.javascript([=[
    (() => {
      const spacer = document.createElement("div");
      spacer.style.height = `${innerHeight * 1.5}px`;
      spacer.dataset.geometrySpacer = "true";
      document.querySelector(".dashboard-page").append(spacer);
      window.scrollTo(0, document.body.scrollHeight);
      return true;
    })()
  ]=]))
  viset.page.wait_for(
    "document.querySelector(\"[data-stage='reward-editor'] .sticky-save-region\").dataset.saveVisible === 'false'",
    "10s"
  )
  viset.page.evaluate(viset.javascript([=[
    (() => {
      document.querySelector("[data-stage='reward-editor']").scrollIntoView({ block: "center" });
      return true;
    })()
  ]=]))
end

local function prepare_modal()
  viset.page.evaluate(viset.javascript([=[
    (() => {
      const newName = document.querySelector("[aria-label='New round type name']");
      newName.value = "Exact answers";
      newName.dispatchEvent(new Event("input", { bubbles: true }));
      document.querySelector("[data-action='create-round-type']").click();
      return true;
    })()
  ]=]))
  viset.page.wait_for("document.querySelectorAll('.studio-chip[aria-pressed]').length >= 2", "10s")
  viset.page.evaluate(viset.javascript([=[
    (() => {
      const name = document.querySelector("input[id^='guess-profile-'][id$='-name']");
      name.value = `${name.value} updated`;
      name.dispatchEvent(new Event("input", { bubbles: true }));
      const choice = [...document.querySelectorAll(".studio-chip")].find(
        button => button.getAttribute("aria-pressed") === "false"
      );
      if (!choice) throw new Error("Another round type was not found");
      choice.click();
      return true;
    })()
  ]=]))
  viset.page.wait_for("Boolean(document.querySelector('[data-unsaved-profile-dialog]'))", "10s")
  viset.page.evaluate(viset.javascript([=[
    (() => {
      document.querySelector(".toast-card")?.click();
      return true;
    })()
  ]=]))
end

local succeeded, failure = pcall(function()
  local device = viset.context.device.name
  local capture = captures[device]
  if capture == nil then
    error("No sticky Save capture is registered for " .. device)
  end
  local theme = capture.theme
  local view = capture.view
  local minimum_target = device:sub(1, 6) == "mobile" and 48 or 44
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(
    base_url .. "/simulation/login?view=" .. capture.login .. "&theme=" .. theme
  )
  wait_for_text("Sample Channel")
  if view == "repeated-editor" then
    prepare_repeated(theme)
  else
    viset.page.wait_for("Boolean(document.querySelector('.sticky-save-region'))", "20s")
  end

  if view == "long-validation-focus" then
    prepare_long_validation()
  elseif view == "dynamic-create" then
    click_text(".studio-stage__header", "Create a reward")
    viset.page.wait_for(
      "[...document.querySelectorAll('button')].some(button => button.textContent.includes('Create reward'))",
      "10s"
    )
    viset.page.evaluate(viset.javascript([=[
      (() => {
        const region = document.querySelector("[data-stage='reward-editor'] .sticky-save-region");
        if (region.dataset.saveActive !== "false") throw new Error("Create enrolled as Save");
        region.scrollIntoView({ block: "end" });
        window.scrollBy(0, 80);
        return true;
      })()
    ]=]))
  elseif view == "dynamic-save" then
    prepare_dynamic_save()
  elseif view == "modal-save" then
    prepare_modal()
  elseif view == "disabled-save" then
    viset.page.evaluate(viset.javascript([=[
      (() => {
        const button = document.querySelector("[aria-label='Save custom commands']");
        if (!button.disabled) throw new Error("Expected stable disabled Save state");
        window.scrollBy(0, 500);
        return true;
      })()
    ]=]))
  end

  viset.sleep("350ms")
  assert_architecture()
  if view ~= "dynamic-create" then
    assert_active_geometry(minimum_target)
  end
  if view == "long-validation-focus" then
    viset.page.evaluate(viset.javascript([=[
      (() => {
        const focus = document.querySelector("#gamblingCooldown").getBoundingClientRect();
        const shelf = document.querySelector(
          ".sticky-save-region[data-save-visible='true'] .sticky-save-region__surface"
        ).getBoundingClientRect();
        if (focus.bottom + 4 > shelf.top) {
          throw new Error(`Focused field is covered: ${focus.bottom}/${shelf.top}`);
        }
        return true;
      })()
    ]=]))
  end
  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
