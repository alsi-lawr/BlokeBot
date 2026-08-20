namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationCelTransformHandler : IAutomationPureNodeHandler
{
    public AutomationCelTransformHandler()
        : this(AutomationDefinitionIds.CelTransform) { }

    internal AutomationCelTransformHandler(AutomationDefinitionId definitionId) =>
        Contract = AutomationCelTransform.HandlerContract(definitionId);

    private readonly AutomationTransformCelService _service = new();
    private int _calls;

    public AutomationPureHandlerContract Contract { get; }

    internal int Calls => Volatile.Read(ref _calls);

    public ValueTask<AutomationPureNodeResult> ExecuteAsync(
        AutomationPureNodeInput input,
        CancellationToken cancellationToken
    )
    {
        _ = Interlocked.Increment(ref _calls);
        return ValueTask.FromResult(
            input.Configuration is AutomationCelTransformConfiguration configuration
                ? _service.Execute(configuration, input.Inputs, cancellationToken)
                : new AutomationPureNodeResult.Failed("configuration-invalid")
        );
    }
}
