---@meta _

---@alias VisetDuration number|string
---@alias VisetTomlValue boolean|number|string|VisetTomlValue[]|table<string, VisetTomlValue>
---@alias VisetJavaScriptValue boolean|number|string|VisetJavaScriptValue[]|table<string, VisetJavaScriptValue>
---@alias VisetJavaScriptArguments VisetJavaScriptValue[]|table<string, VisetJavaScriptValue>
---@alias VisetProcessHandle integer
---@alias VisetEasing "linear"|"in_sine"|"out_sine"|"in_out_sine"|string

---@class VisetDimensions
---@field width integer
---@field height integer

---@class VisetDevice
---@field name string
---@field mobile boolean
---@field touch boolean
---@field device_scale number
---@field viewport VisetDimensions
---@field frame? VisetDimensions

---@class VisetContext
---@field script_path string
---@field output string
---@field device VisetDevice
---@field axes table<string, VisetTomlValue>
---@field data table<string, VisetTomlValue>

---@class VisetScript
---@field directory string

---@class VisetProcessStartOptions
---@field file string
---@field arguments? string[]
---@field working_directory? string
---@field environment? table<string, string>

---@class VisetProcessResult
---@field exit_code integer
---@field stdout string
---@field stderr string

---@class VisetProcess
---@field start fun(options: VisetProcessStartOptions): VisetProcessHandle
---@field wait fun(handle: VisetProcessHandle, timeout?: VisetDuration): VisetProcessResult
---@field stop fun(handle: VisetProcessHandle): VisetProcessResult

---@class VisetHttpRequest
---@field url string
---@field headers? table<string, string>
---@field timeout? VisetDuration

---@class VisetHttpResponse
---@field status integer
---@field headers table<string, string>
---@field body string

---@class VisetHttp
---@field get fun(options: VisetHttpRequest): VisetHttpResponse
---@field wait fun(options: VisetHttpRequest): VisetHttpResponse

---@class VisetAnimationOptions
---@field duration VisetDuration
---@field easing? VisetEasing
---@field update string JavaScript synchronous function receiving the animation frame state.

---@class VisetPage
---@field navigate fun(url: string)
---@field evaluate fun(script: string, arguments?: VisetJavaScriptArguments): any
---@field wait_for fun(expression: string, timeout: VisetDuration)
---@field animate fun(options: VisetAnimationOptions)

---@class VisetEmulation
---@field apply fun(device: VisetDevice)
---@field touch fun(x: number, y: number)

---@class VisetRecording
---@field start fun(self: VisetRecording)
---@field stop fun(self: VisetRecording)
---@field during fun(self: VisetRecording, duration: VisetDuration, callback?: fun())

---@class VisetApi
---@field api_version 1
---@field context VisetContext
---@field script VisetScript
---@field process VisetProcess
---@field http VisetHttp
---@field page VisetPage
---@field emulation VisetEmulation
---@field javascript fun(source: string): string Marks an embedded JavaScript string for editor tooling.
---@field sleep fun(duration: VisetDuration)
---@field snapshot fun()
---@field record fun(): VisetRecording

---@type VisetApi
---@diagnostic disable-next-line: missing-fields
viset = {}
