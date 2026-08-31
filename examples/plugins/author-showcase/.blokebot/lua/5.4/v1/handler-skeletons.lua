-- Generated from plugin.toml. Regenerate with blokebot-plugin generate; do not edit.
local modules = {}

---@type ExamplesAuthorShowcaseMainHandlers
local module_1 = {
  ["automation_action"] = function(input)
    return {}
  end,
  ["automation_source"] = function(input)
    return { ["message"] = "" }
  end,
  ["host_calls"] = function(input)
    return input
  end,
  ["migrate"] = function(input)
    return input
  end,
  ["on_action"] = function(input)
    return input
  end,
  ["on_event"] = function(input)
    return input
  end,
  ["on_schedule"] = function(input)
    return input
  end,
  ["on_webhook"] = function(input)
    return { status = 200, body = "" }
  end,
  ["render_page"] = function(input)
    return {}
  end,
}
modules["main"] = module_1

return modules
