--[[
# viset
version = 1
output_root = "output/sticky-save-ownership-probe"
output = "{device}.png"
browser_arguments = [
  "--disable-background-networking",
  "--disable-background-mode",
  "--disable-component-update",
  "--disable-default-apps",
  "--disable-sync",
  "--force-prefers-reduced-motion",
  "--host-resolver-rules=MAP * 0.0.0.0, EXCLUDE 127.0.0.1",
  "--metrics-recording-only",
  "--password-store=basic",
  "--use-mock-keychain",
]

[devices.browser-probe]
mobile = false
touch = false
device_scale = 1.0

[devices.browser-probe.viewport]
width = 800
height = 600

]]

local repo_root = viset.script.directory .. "/.."
local port = os.getenv("BLOKEBOT_STICKY_SAVE_PROBE_PORT") or "43233"
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
  viset.http.wait({ url = base_url .. "/simulation/ready", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/ready")
  viset.page.evaluate(viset.javascript([=[
    (async () => {
      document.body.innerHTML = `
        <main>
          <section data-sticky-save-scope id="first-boundary" style="height: 180px">
            <div class="sticky-save-region"
                 data-save-active="true"
                 data-save-scope="editor"
                 data-save-visible="false"
                 style="height: 48px">
              <div class="sticky-save-region__surface"><button>First</button></div>
            </div>
          </section>
          <section data-sticky-save-scope id="second-boundary" style="height: 180px">
            <div class="sticky-save-region"
                 data-save-active="true"
                 data-save-scope="editor"
                 data-save-visible="false"
                 style="height: 48px">
              <div class="sticky-save-region__surface"><button>Second</button></div>
            </div>
          </section>
        </main>`;
      const module = await import("/Components/Layout/StickySaveRegion.razor.js");
      const first = document.querySelector("#first-boundary .sticky-save-region");
      const second = document.querySelector("#second-boundary .sticky-save-region");
      const registrations = [module.register(first), module.register(second)];
      document.querySelector("#second-boundary").dispatchEvent(
        new PointerEvent("pointerdown", { bubbles: true })
      );
      window.stickySaveOwnershipProbe = { first, registrations, second };
      return true;
    })()
  ]=]))
  viset.page.wait_for(
    "window.stickySaveOwnershipProbe?.second.dataset.saveVisible === 'true'",
    "10s"
  )
  viset.sleep("100ms")
  viset.page.evaluate(viset.javascript([=[
    (() => new Promise((resolve, reject) => {
      const probe = window.stickySaveOwnershipProbe;
      const boundary = probe.second.closest("[data-sticky-save-scope]");
      const assertion = new MutationObserver(() => {
        assertion.disconnect();
        if (
          probe.first.dataset.saveVisible !== "true" ||
          probe.second.dataset.saveVisible !== "false"
        ) {
          reject(new Error("Inert owner did not transfer during mutation delivery"));
          return;
        }
        resolve(true);
      });
      assertion.observe(boundary, { attributes: true, attributeFilter: ["inert"] });
      boundary.setAttribute("inert", "");
    }))()
  ]=]))
  viset.page.evaluate(viset.javascript([=[
    (() => {
      for (const registration of window.stickySaveOwnershipProbe.registrations) {
        registration.dispose();
      }
      delete window.stickySaveOwnershipProbe;
      document.body.replaceChildren();
      return true;
    })()
  ]=]))
  viset.snapshot()
end)

viset.process.stop(server)
if not succeeded then
  error(failure, 0)
end
