---@type ExamplesUpdateFailureMainHandlers
local handlers = { migrate = function() error("example migration failed") end }

return handlers
