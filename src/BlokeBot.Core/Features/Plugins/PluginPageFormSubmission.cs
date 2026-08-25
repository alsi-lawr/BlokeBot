using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

public sealed record PluginPageFormSubmission(PluginActionId Action, PluginValue.Map Input);
