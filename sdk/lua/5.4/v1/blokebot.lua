---@meta

-- Generated from BlokeBot host API v1 for Lua 5.4.
-- Regenerate the author artifacts instead of editing this file.

---@alias BlokeBotValue nil|boolean|number|string|BlokeBotValue[]|table<string, BlokeBotValue>

---@class BlokeBotHost
local host = {}

---@overload fun(module: "diagnostics", operation: "log", argument1: string, argument2: string): nil
---@overload fun(module: "responses", operation: "chat", argument1: string): nil
---@overload fun(module: "responses", operation: "whisper", argument1: string): nil
---@overload fun(module: "chat", operation: "send", argument1: string): nil
---@overload fun(module: "overlay", operation: "play-cue", argument1: string, argument2: string): nil
---@overload fun(module: "points", operation: "add", argument1: string, argument2: string, argument3: string): string
---@overload fun(module: "twitch", operation: "create-marker", argument1: string): nil
---@overload fun(module: "schedules", operation: "once", argument1: string, argument2: string, argument3: table<string, BlokeBotValue>): string
---@overload fun(module: "schedules", operation: "recurring", argument1: string, argument2: string, argument3: number, argument4: table<string, BlokeBotValue>): string
---@overload fun(module: "schedules", operation: "cancel", argument1: string): nil
---@overload fun(module: "storage", operation: "execute", argument1: string, argument2: table<string, BlokeBotValue>): number
---@overload fun(module: "storage", operation: "query", argument1: string, argument2: table<string, BlokeBotValue>): BlokeBotValue[]
---@overload fun(module: "http", operation: "send", argument1: table<string, BlokeBotValue>): table<string, BlokeBotValue>
---@param module string
---@param operation string
---@param ... BlokeBotValue
---@return BlokeBotValue
function host.call(module, operation, ...) end

---@class BlokeBot
---@field host BlokeBotHost
blokebot = { host = host }
