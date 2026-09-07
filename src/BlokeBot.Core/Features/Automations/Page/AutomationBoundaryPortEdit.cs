using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

public sealed record AutomationBoundaryPortEdit(
    AutomationEditorNode Node,
    ImmutableArray<AutomationPortMetadata> Ports
);
