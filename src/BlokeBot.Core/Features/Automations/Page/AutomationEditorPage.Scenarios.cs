using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private AutomationScenarioEditor? _scenario;
    private ImmutableArray<AutomationScenarioSnapshot> _savedScenarios = [];
    private ImmutableArray<AutomationPortMetadata> _scenarioFields = [];
    private readonly HashSet<string> _invalidFixtureInputs = [];
    private CancellationTokenSource? _scenarioCancellation;

    private async Task OpenScenariosAsync()
    {
        if (_editor?.Subflow is not null)
        {
            return;
        }
        await OpenAuthoringAsync(AutomationAuthoringTask.Scenarios);
        if (_scenario is null)
        {
            NewScenario();
        }
        await LoadScenariosAsync();
    }

    private async Task LoadScenariosAsync()
    {
        var editor = _editor;
        var host = HostId;
        var saved = editor?.Id is { } id
            ? await _scenariosService.ListAsync(new(host), id, CancellationToken.None)
            : [];
        if (ReferenceEquals(editor, _editor) && host == HostId)
        {
            _savedScenarios = saved;
        }
    }

    private void NewScenario()
    {
        _invalidFixtureInputs.Clear();
        _scenario =
            _editor is not null && SampleSourceId() is { } source
                ? new(_scenariosService.CreateDefaultFixture(_editor.Draft(new(HostId)), source))
                : null;
        RefreshScenarioFields();
    }

    private void SelectScenario(string id)
    {
        var saved = _savedScenarios.FirstOrDefault(value => value.Id.Value.ToString() == id);
        if (saved is null)
        {
            NewScenario();
            return;
        }
        _scenario = new(saved.Fixture) { Id = saved.Id, Name = saved.Name };
        _invalidFixtureInputs.Clear();
        RefreshScenarioFields();
    }

    private void SelectScenarioSource(string id)
    {
        if (
            _editor is null
            || _scenario is null
            || !Guid.TryParse(id, out var parsed)
            || !_editor.Nodes.Any(node =>
                node.Id.Value == parsed && node.Definition.Kind == AutomationNodeKind.Source
            )
        )
        {
            return;
        }
        _scenario.Replace(
            _scenariosService.CreateDefaultFixture(
                _editor.Draft(new(HostId)),
                new(parsed),
                _scenario.Fixture.Seed
            )
        );
        _invalidFixtureInputs.Clear();
        RefreshScenarioFields();
    }

    private void RefreshScenarioFields() =>
        _scenarioFields =
            _editor is not null && _scenario is not null
                ? _scenariosService.SourceFields(
                    _editor.Draft(new(HostId)),
                    _scenario.Fixture.SourceNodeId
                )
                : [];

    private void SetFixtureValidity(AutomationFixtureInputValidity validity)
    {
        if (validity.Valid)
        {
            _ = _invalidFixtureInputs.Remove(validity.Key);
        }
        else
        {
            _ = _invalidFixtureInputs.Add(validity.Key);
        }
    }

    private void GenerateScenarioFixture()
    {
        if (_editor is null || _scenario is null)
        {
            return;
        }
        var fixture = _scenario.Fixture;
        var inputs = fixture
            .ConnectedInputs.Select(input =>
            {
                var port = _editor
                    .Nodes.FirstOrDefault(node => node.Id == input.NodeId)
                    ?.Definition.Inputs.FirstOrDefault(port => port.Id == input.PortId);
                return port is null
                    ? null
                    : new AutomationScenarioGeneratedInput(
                        input.NodeId,
                        input.PortId,
                        port.ValueType,
                        port.Nullability,
                        port.Sensitivity,
                        AutomationValueProvenance.Generated
                    );
            })
            .OfType<AutomationScenarioGeneratedInput>()
            .ToImmutableArray();
        if (inputs.Any(input => input.Sensitivity != AutomationDataSensitivity.Safe))
        {
            _feedback = "Remove sensitive input overrides before generating a fixture.";
            return;
        }
        _scenario.Replace(
            _scenariosService.CreatePortableFixture(
                _editor.Draft(new(HostId)),
                fixture.SourceNodeId,
                fixture.Seed,
                fixture.ClockUtc,
                inputs,
                fixture.Effects
            )
        );
        _invalidFixtureInputs.Clear();
    }

    private async Task RunScenarioAsync()
    {
        if (_editor is null || _scenario is null || _invalidFixtureInputs.Count > 0 || _busy)
        {
            return;
        }
        var editor = _editor;
        var version = _draftRevision;
        var scenario = _scenario;
        var fixture = scenario.Fixture;
        var draft = editor.Draft(new(HostId));
        var host = HostId;
        _busy = true;
        _scenarioCancellation = new();
        try
        {
            var outcome = await RunAuthoringMutationAsync(
                host,
                () => _scenariosService.RunAsync(draft, fixture, _scenarioCancellation.Token)
            );
            if (
                !ReferenceEquals(scenario, _scenario)
                || !ReferenceEquals(editor, _editor)
                || version != _draftRevision
                || host != HostId
            )
            {
                return;
            }
            AutomationTraceId? trace = null;
            switch (outcome)
            {
                case AutomationScenarioRunOutcome.Completed completed:
                    _sampleOutcomes = completed.Nodes;
                    trace = completed.TraceId;
                    _traceTitle = "Test run succeeded";
                    break;
                case AutomationScenarioRunOutcome.Failed failed:
                    _sampleOutcomes = failed.Nodes;
                    trace = failed.TraceId;
                    _traceTitle = "Test run failed";
                    break;
                case AutomationScenarioRunOutcome.Invalid invalid:
                    ShowValidation(invalid.Errors, "Correct the scenario or draft.");
                    break;
                default:
                    ShowUnavailable();
                    break;
            }
            if (trace is { } id)
            {
                await LoadTraceAsync(id);
            }
        }
        catch (OperationCanceledException) when (_scenarioCancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(editor, _editor))
            {
                _feedback = "Test cancelled.";
            }
        }
        finally
        {
            _scenarioCancellation.Dispose();
            _scenarioCancellation = null;
            _busy = false;
        }
    }

    private async Task SaveScenarioAsync()
    {
        if (_editor?.Id is not { } flow || _scenario is null || _invalidFixtureInputs.Count > 0)
        {
            return;
        }
        var selected = _scenario;
        var host = HostId;
        var outcome = await RunAuthoringMutationAsync(
            host,
            () =>
                _scenariosService.SaveAsync(
                    new(host),
                    flow,
                    selected.Id,
                    selected.Name,
                    selected.Fixture,
                    CancellationToken.None
                )
        );
        if (outcome is null || !ReferenceEquals(selected, _scenario) || host != HostId)
        {
            return;
        }
        if (outcome.Status == AutomationScenarioAuthoringStatus.Saved)
        {
            selected.Id = outcome.Id;
            await LoadScenariosAsync();
            _feedback = "Scenario saved.";
        }
        else
        {
            ShowScenarioFailure(outcome);
        }
    }

    private async Task DuplicateScenarioAsync()
    {
        if (_editor?.Id is not { } flow || _scenario?.Id is not { } id)
        {
            return;
        }
        var host = HostId;
        var selected = _scenario;
        var outcome = await RunAuthoringMutationAsync(
            host,
            () =>
                _scenariosService.DuplicateAsync(
                    new(host),
                    flow,
                    id,
                    selected.Name + " copy",
                    CancellationToken.None
                )
        );
        if (outcome is null || host != HostId || !ReferenceEquals(selected, _scenario))
        {
            return;
        }
        await LoadScenariosAsync();
        if (outcome.Id is { } created)
        {
            SelectScenario(created.Value.ToString());
        }
        else
        {
            ShowScenarioFailure(outcome);
        }
    }

    private async Task DeleteScenarioAsync()
    {
        if (_editor?.Id is not { } flow || _scenario?.Id is not { } id)
        {
            return;
        }
        var host = HostId;
        var selected = _scenario;
        var outcome = await RunAuthoringMutationAsync(
            host,
            () => _scenariosService.DeleteAsync(new(host), flow, id, CancellationToken.None)
        );
        if (outcome is null || host != HostId || !ReferenceEquals(selected, _scenario))
        {
            return;
        }
        if (outcome.Status == AutomationScenarioAuthoringStatus.Deleted)
        {
            NewScenario();
            await LoadScenariosAsync();
        }
        else
        {
            ShowScenarioFailure(outcome);
        }
    }

    private void ShowScenarioFailure(AutomationScenarioAuthoringOutcome outcome)
    {
        if (!outcome.Errors.IsDefaultOrEmpty)
        {
            ShowValidation(outcome.Errors, "Scenario not saved.");
        }
        else
        {
            _feedback = $"Scenario not saved: {outcome.Status}.";
            _operationFailed = true;
        }
    }
}
