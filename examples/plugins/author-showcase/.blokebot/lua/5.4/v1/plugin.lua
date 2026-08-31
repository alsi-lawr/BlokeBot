---@meta

-- Generated from plugin.toml. Regenerate with blokebot-plugin generate; do not edit.

---@class ExamplesAuthorShowcaseInstallationSettings
---@field ["api-token"]? BlokeBotProtectedValue # Supplies a private example value.

---@class BlokeBotInstallationSettings: ExamplesAuthorShowcaseInstallationSettings

---@class ExamplesAuthorShowcaseShowcaseFeatureSettings
---@field ["enabled"] boolean # Controls the example feature.
---@field ["message"] string # Sets the example response.

---@class BlokeBotFeatureSettings
---@field ["enabled"]? boolean # Controls the example feature.
---@field ["message"]? string # Sets the example response.

---@class ExamplesAuthorShowcaseExampleSourceInputValues

---@class ExamplesAuthorShowcaseExampleSourceInput
---@field configuration table<string, BlokeBotValue>
---@field inputs ExamplesAuthorShowcaseExampleSourceInputValues

---@class ExamplesAuthorShowcaseExampleSourceOutput
---@field ["message"] string # Message

---@class ExamplesAuthorShowcaseExampleActionInputValues
---@field ["message"] string # Message

---@class ExamplesAuthorShowcaseExampleActionInput
---@field configuration table<string, BlokeBotValue>
---@field inputs ExamplesAuthorShowcaseExampleActionInputValues

---@class ExamplesAuthorShowcaseExampleActionOutput

---@class ExamplesAuthorShowcaseHostChangeEventInput: table<string, BlokeBotValue>
---@field event_id string
---@field source string

---@class ExamplesAuthorShowcaseHandlers
---@field ["host_calls"] fun(input: BlokeBotCommandInput): BlokeBotValue
---@field ["on_event"] fun(input: ExamplesAuthorShowcaseHostChangeEventInput): BlokeBotValue
---@field ["on_schedule"] fun(input: BlokeBotScheduleInput): BlokeBotValue
---@field ["on_webhook"] fun(input: BlokeBotWebInput): BlokeBotValue
---@field ["on_action"] fun(input: BlokeBotWebInput): BlokeBotValue
---@field ["migrate"] fun(input: BlokeBotValue): BlokeBotValue
---@field ["render_page"] fun(input: BlokeBotPageInput): BlokeBotValue
---@field ["automation_source"] fun(input: ExamplesAuthorShowcaseExampleSourceInput): ExamplesAuthorShowcaseExampleSourceOutput
---@field ["automation_action"] fun(input: ExamplesAuthorShowcaseExampleActionInput): ExamplesAuthorShowcaseExampleActionOutput

---@class ExamplesAuthorShowcaseMainHandlers
---@field ["host_calls"] fun(input: BlokeBotCommandInput): BlokeBotValue
---@field ["on_event"] fun(input: ExamplesAuthorShowcaseHostChangeEventInput): BlokeBotValue
---@field ["on_schedule"] fun(input: BlokeBotScheduleInput): BlokeBotValue
---@field ["on_webhook"] fun(input: BlokeBotWebInput): BlokeBotValue
---@field ["on_action"] fun(input: BlokeBotWebInput): BlokeBotValue
---@field ["migrate"] fun(input: BlokeBotValue): BlokeBotValue
---@field ["render_page"] fun(input: BlokeBotPageInput): BlokeBotValue
---@field ["automation_source"] fun(input: ExamplesAuthorShowcaseExampleSourceInput): ExamplesAuthorShowcaseExampleSourceOutput
---@field ["automation_action"] fun(input: ExamplesAuthorShowcaseExampleActionInput): ExamplesAuthorShowcaseExampleActionOutput
