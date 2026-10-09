--[[
# viset
version = 1
output_root = "../src/BlokeBot.Site/wwwroot/media/full-overlays"
output = "{device}-{theme}-full-overlay-editor.png"
frame = "builtin:auto"
browser_arguments = [
  "--disable-background-networking", "--disable-component-update", "--disable-sync",
  "--force-prefers-reduced-motion", "--hide-scrollbars",
  "--host-resolver-rules=MAP * 0.0.0.0, EXCLUDE 127.0.0.1",
]
[devices.laptop]
mobile = false
touch = false
device_scale = 1.0
[devices.laptop.viewport]
width = 1600
height = 1080
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
local port = os.getenv("BLOKEBOT_CAPTURE_PORT") or "5479"
local base_url = "http://127.0.0.1:" .. port
local server = nil
local reachable = pcall(function()
  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "3s" })
end)
if not reachable then
  server = viset.process.start({
    file = os.getenv("BLOKEBOT_DOTNET") or "dotnet",
    arguments = {
      "run", "--project", repo_root .. "/src/BlokeBot.Simulation/BlokeBot.Simulation.csproj",
      "--configuration", "Release", "--no-build", "--no-launch-profile", "--", "--urls", base_url,
    },
    working_directory = repo_root,
    environment = {
      DOTNET_CLI_TELEMETRY_OPTOUT = "1", TZ = "UTC",
      BlokeBot__StateDirectory = os.getenv("BLOKEBOT_CAPTURE_STATE_DIRECTORY")
        or repo_root .. "/.agent-workspace/full-overlay-capture/" .. port,
    },
  })
end

local function js(code)
  return viset.page.evaluate(viset.javascript("(async () => {" .. code .. "})()"))
end
local function wait(code)
  viset.page.wait_for(viset.javascript(code), "60s")
end

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  viset.http.wait({ url = base_url .. "/simulation/started", timeout = "90s" })
  viset.page.navigate(base_url .. "/simulation/login?theme=" .. theme)
  wait("document.querySelector('.account-menu__summary') !== null")
  local route = js([[
    const response = await fetch('/simulation/full-overlay', {
      method: 'POST', headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({
        html: `<main class="stream-scene">
  <header class="stream-heading">
    <p>COMMUNITY NIGHT</p>
    <h1>Building together</h1>
  </header>
  <div class="stream-widgets">
    {{widgets}}
  </div>
  <footer>Thanks for being here. Your next adventure starts on stream.</footer>
</main>`,
        css: '.stream-scene{box-sizing:border-box;min-height:100vh;padding:48px;font-family:system-ui;color:#f8fafc;background:#172033}.stream-heading{border-left:4px solid #8bd5ca;padding:12px 24px}.stream-heading p{color:#8bd5ca;letter-spacing:.15em}.stream-heading h1{font-size:48px;margin:8px 0}.stream-widgets{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px;margin:32px 0}.stream-widgets>section{background:#23304a;padding:20px;border-radius:14px;min-height:120px}footer{color:#d0d7e2;font-size:18px}',
        kinds: ['giveaway','community-goal','viewer-queue','event-feed']
      })
    });
    const fixture = await response.json();
    return '/full-overlays/' + fixture.overlayId + '/edit';
  ]])
  viset.page.navigate(base_url .. route .. "?simulationTheme=" .. theme)
  wait("document.querySelector('[data-source=html]')?.value.includes('Building together') === true")
  wait("document.querySelector('.full-editor-preview-state')?.textContent.includes('Unsaved') === true")
  js([[ [...document.querySelectorAll('[role=treeitem]')].find(e => e.textContent.includes('section')).click(); ]])
  wait("document.querySelector('[role=treeitem][aria-selected=true]')?.textContent.includes('section') === true")
  js([[ const name = document.getElementById('full-overlay-name'); name.value='Community night'; name.dispatchEvent(new Event('input',{bubbles:true})); ]])
  js([[ document.querySelector('button[aria-label="Save draft"]').click(); ]])
  wait("document.querySelector('.full-editor-feedback')?.textContent.includes('Draft saved.') === true")
  js([[
    window.captureFullPreview = null;
    window.addEventListener('message', event => {
      const preview = document.querySelector('[data-preview-frame]');
      const value = event.data;
      if (event.source === preview?.contentWindow && event.origin === location.origin
          && value?.kind === 'blokebot-full-observations'
          && preview.getAttribute('src') === '/full-overlays/preview/' + value.previewId)
        window.captureFullPreview = value;
    });
    document.querySelector('button[aria-label="Replay preview"]').click();
  ]])
  wait([[(() => {
    const preview = document.querySelector('[data-preview-frame]');
    const current = window.captureFullPreview;
    const selected = document.querySelector('[data-selection]');
    return current && preview.getAttribute('src') === '/full-overlays/preview/' + current.previewId
      && current.viewport.width > 0 && current.viewport.height > 0
      && current.items.filter(item => item.key.startsWith('widget:') && item.width > 0 && item.height > 0).length === 4
      && current.items.some(item => item.styles['font-size'] === '48px' && item.width > 0 && item.height > 0)
      && selected && !selected.hidden && selected.getBoundingClientRect().width > 0;
  })()]])
  if viset.context.device.name == "phone" then
    js([[ document.getElementById('full-mode-source-tab').click(); ]])
    wait("document.querySelector('[data-source=html]').getBoundingClientRect().height > 0")
    js([[ const source=document.querySelector('[data-source=html]'); source.setSelectionRange(0,0); source.focus({preventScroll:true}); source.scrollTop=0; source.scrollLeft=0; window.scrollTo(0,0); ]])
  end
  wait("!document.querySelector('#components-reconnect-modal.components-reconnect-show') && getComputedStyle(document.querySelector('main')).opacity === '1'")
  viset.snapshot()
end)
if server ~= nil then viset.process.stop(server) end
if not succeeded then error(failure, 0) end
