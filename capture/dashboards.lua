--[[
# viset
version = 1
output_root = "../.agent-workspace/dashboard-capture/raw"
output = "{device}-{theme}-{operation}.webp"
frame = "builtin:auto"
frames_per_second = 12
browser_arguments = [
  "--disable-background-networking", "--disable-background-mode",
  "--disable-component-update", "--disable-default-apps", "--disable-sync",
  "--host-resolver-rules=MAP * 0.0.0.0, EXCLUDE 127.0.0.1",
  "--hide-scrollbars", "--metrics-recording-only", "--password-store=basic",
  "--use-mock-keychain",
]

[devices.laptop]
mobile = false
touch = false
device_scale = 1.0
[devices.laptop.viewport]
width = 1280
height = 900

[devices.phone]
mobile = true
touch = true
device_scale = 1.0
[devices.phone.viewport]
width = 390
height = 900

[matrix]
theme = ["light", "dark"]
operation = ["editor", "control"]
]]

-- The runner owns an isolated Simulation. Never attach to a human preview.
local port = assert(os.getenv("BLOKEBOT_CAPTURE_PORT"), "Use capture-dashboards.sh")
local base = "http://127.0.0.1:" .. port
local theme = viset.context.axes.theme
local phone = viset.context.device.name == "phone"
local arrangement = phone and "Phone" or "Desktop"
local width = phone and 4 or 6
local dashboard = "d3390000-0000-4000-8000-000000000007"
local function js(code)
  return viset.page.evaluate(viset.javascript("(() => {" .. code .. "})()"))
end
local function wait(code)
  viset.page.wait_for(viset.javascript(code), "30s")
end
local function pause()
  viset.sleep("450ms")
end
local function click(selector)
  js("const e=document.querySelector(" .. string.format("%q", selector) .. "); if(!e || e.disabled) throw Error('Unavailable control: ' + " .. string.format("%q", selector) .. "); e.scrollIntoView({block:'center'}); e.click();")
  pause()
end
local function input(selector, value, event)
  js("const e=document.querySelector(" .. string.format("%q", selector) .. "); if(!e || e.disabled) throw Error('Unavailable input'); e.scrollIntoView({block:'center'}); e.value=" .. string.format("%q", tostring(value)) .. "; e.dispatchEvent(new Event('" .. (event or "change") .. "',{bubbles:true}));")
  pause()
end
local function route(mode)
  viset.page.navigate(base .. "/dashboards/" .. mode .. "/" .. dashboard .. "?simulationTheme=" .. theme)
  wait("document.querySelectorAll('.dashboard-card').length === 7 && document.querySelector('#dashboard-arrangement') !== null")
  input("#dashboard-arrangement", arrangement)
  wait("document.querySelector('[data-dashboard-grid]')?.dataset.arrangement === '" .. arrangement .. "'")
end
local function settings()
  click('button[aria-label="Configure Stream announcement"]')
  wait("document.querySelector('dialog[data-dashboard-settings][open] input[id$=\"-label\"]') !== null")
  click("dialog[open] .dashboard-placement summary")
end
local function place(field, value)
  input('dialog[open] [data-placement-field="' .. field .. '"]', value)
end
local function closeSettings()
  click('button[aria-label="Close settings"]')
  wait("document.querySelector('dialog[data-dashboard-settings][open]') === null")
end
local function save()
  click('button[aria-label="Save dashboard"]')
  wait("document.querySelector('.dashboard-composition')?.dataset.serverDirty === 'false' && document.querySelector('.dashboard-save-state')?.textContent === 'Saved'")
  js("window.scrollTo(0,0);")
end
local function card(name)
  return "[...document.querySelectorAll('.dashboard-card')].find(e=>e.querySelector('.task-panel__title')?.textContent.trim()===" .. string.format("%q", name) .. ")"
end
local function cardClick(name, button)
  js("const card=" .. card(name) .. "; const e=[...card.querySelectorAll('button')].find(e=>e.textContent.trim()===" .. string.format("%q", button) .. " || e.getAttribute('aria-label')===" .. string.format("%q", button) .. "); if(!e || e.disabled) throw Error('Unavailable native operation'); card.scrollIntoView({block:'center'}); e.click();")
  pause()
end

viset.http.wait({url=base .. "/simulation/ready", timeout="60s"})
viset.page.navigate(base .. "/simulation/login?view=dashboard-controls&theme=" .. theme)
wait("document.querySelectorAll('.dashboard-card').length === 7")
-- Reset through supported UI before each matrix item, not through fixture mutation.
if js("return " .. card("Break countdown") .. "?.querySelector('[data-countdown-deadline]') !== null;") then
  cardClick("Break countdown", "Cancel countdown")
end
if js("return [..." .. card("Next game") .. ".querySelectorAll('button')].some(e=>e.textContent.trim()==='End poll');") then
  cardClick("Next game", "End poll")
end
route("editor")
settings()
place("Column", 1)
place("Width", width)
place("Row", 1)
place("Height", 5)
if js("return document.querySelector('dialog[open] input[type=checkbox]').checked;") then
  click('dialog[open] input[type=checkbox]')
end
closeSettings()
save()
viset.sleep("1s")

print("Recording " .. viset.context.device.name .. " " .. theme .. " " .. viset.context.axes.operation)
local recording = viset.record()
if viset.context.axes.operation == "editor" then
  recording:start()
  recording:during("1500ms")
  settings()
  recording:during("1500ms")
  place("Width", width - 1)
  recording:during("900ms")
  place("Column", 2)
  recording:during("1200ms")
  click('dialog[open] input[type=checkbox]')
  wait("document.querySelector('dialog[open] input[id$=\"-manual-text\"]') !== null")
  input('dialog[open] input[id$="-manual-text"]', "Tea is ready", "input")
  recording:during("1800ms")
  closeSettings()
  js("window.scrollTo(0,0);")
  recording:during("1600ms")
  save()
  recording:during("1800ms")
  recording:stop()
  -- Evidence of the recorded Save is checked after reload, outside the clip.
  route("editor")
  settings()
  wait("document.querySelector('dialog[open] input[id$=\"-manual-text\"]')?.value === 'Tea is ready' && document.querySelector('dialog[open] [data-placement-field=\"Column\"]')?.value === '2' && document.querySelector('dialog[open] [data-placement-field=\"Width\"]')?.value === '" .. (width - 1) .. "'")
  closeSettings()
else
  route("control")
  js("window.scrollTo(0,0);")
  recording:start()
  recording:during("1500ms")
  js("const c=" .. card("Break countdown") .. "; c.scrollIntoView({block:'center'}); const e=c.querySelector('input[type=number]'); e.value='90'; e.dispatchEvent(new Event('change',{bubbles:true}));")
  pause()
  print("Countdown Start")
  cardClick("Break countdown", "Start")
  wait(card("Break countdown") .. ".querySelector('[data-native-status]')?.textContent === 'Running'")
  local first = js("return " .. card("Break countdown") .. ".querySelector('[data-countdown-deadline]').textContent;")
  recording:during("3200ms")
  assert(js("return " .. card("Break countdown") .. ".querySelector('[data-countdown-deadline]').textContent;") ~= first, "Countdown must genuinely tick")
  print("Countdown tick observed; Cancel")
  cardClick("Break countdown", "Cancel countdown")
  wait(card("Break countdown") .. ".querySelector('[data-native-status]')?.textContent === 'Cancelled'")
  recording:during("1200ms")
  print("Poll Start")
  cardClick("Next game", "Start poll")
  wait(card("Next game") .. ".querySelectorAll('.dashboard-result-row').length === 2 && " .. card("Next game") .. ".querySelector('[data-native-status]')?.textContent === 'Active'")
  recording:during("2300ms")
  print("Poll End")
  cardClick("Next game", "End poll")
  wait(card("Next game") .. ".querySelector('[data-native-status]')?.textContent === 'No active poll'")
  recording:during("1600ms")
  recording:stop()
end
