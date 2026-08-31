-- Generated from plugin.toml. Regenerate with blokebot-plugin generate; do not edit.
local modules = {}

---@type ExamplesWorkerCrashMainHandlers
local module_1 = {
  ["crash"] = function(input)
    return input
  end,
}
modules["main"] = module_1

return modules
