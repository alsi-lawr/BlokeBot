using BlokeBot.Core.Features.Automations;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task SubflowMetadata_PublicationAndCallerDeltasUseTheirOwnPublishedSnapshots()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var service = Subflows(f);
        var port = SubflowPort("stable", AutomationPortValueType.Text) with
        {
            Name = "Original label",
            Description = "Independent original description",
            Nullability = AutomationPortNullability.Nullable,
        };
        var draft = Subflow(f.HostId, new([port], []));
        var first = await Publish(service, draft);
        var caller = Caller(
            f.HostId,
            first,
            System.Collections.Immutable.ImmutableDictionary<
                AutomationPortId,
                AutomationValue
            >.Empty.Add(port.Id, new AutomationValue.Text("Retain me"))
        );
        var flow = (await f.Flows.SaveAsync(caller, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var renamed = port with
        {
            Name = "Clearer label",
            Description = "New explanatory description",
        };
        var metadataDraft = Subflow(f.HostId, new([renamed], [])) with { Id = first.SubflowId };
        var metadata = (
            await service.PreviewAsync(metadataDraft, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        metadata.IncompatibleCallers.ShouldBeEmpty();
        metadata.PreviousInterface!.Inputs.ShouldBe([port]);
        var second = await Publish(service, metadataDraft);
        var copy = (
            await f.Flows.DuplicateAsync(new(f.HostId), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowDuplicateOutcome.Duplicated>();
        await using (var copiedDb = await f.Database.CreateDbContextAsync())
        {
            var copied = await copiedDb.AutomationFlowNodes.SingleAsync(n =>
                n.FlowId == copy.FlowId.Value
                && n.DefinitionId == AutomationSubflowDefinitions.Invoke
            );
            copied.Id.ShouldNotBe(caller.Nodes[1].Id.Value);
            AutomationSubflowDefinitions
                .TryRead(
                    new(
                        copied.DefinitionId,
                        copied.DefinitionSchemaVersion,
                        System.Text.Json.JsonDocument.Parse(copied.ConfigurationJson).RootElement
                    ),
                    out var copiedCall
                )
                .ShouldBeTrue();
            copiedCall.Interface.Inputs.ShouldBe([port]);
        }

        var nestedDraft = Nesting(Subflow(f.HostId), second);
        nestedDraft = nestedDraft with
        {
            Graph = nestedDraft.Graph with
            {
                Nodes = nestedDraft.Graph.Nodes.SetItem(
                    1,
                    Invoke(
                        second,
                        nodeId: nestedDraft.Graph.Nodes[1].Id,
                        values: System.Collections.Immutable.ImmutableDictionary<
                            AutomationPortId,
                            AutomationValue
                        >.Empty.Add(port.Id, new AutomationValue.Text("Nested retained"))
                    )
                ),
            },
        };
        var nested = await Publish(service, nestedDraft);
        var changed = renamed with
        {
            ValueType = AutomationPortValueType.Number,
            Sensitivity = AutomationDataSensitivity.Sensitive,
        };
        var added = SubflowPort("added", AutomationPortValueType.Text) with
        {
            Nullability = AutomationPortNullability.Nullable,
        };
        var structural = Subflow(f.HostId, new([changed, added], [])) with { Id = first.SubflowId };
        var preview = (
            await service.PreviewAsync(structural, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        preview.PreviousInterface!.Inputs.ShouldBe([renamed]);
        preview.IncompatibleCallers.ShouldContain(c =>
            c.FlowId == flow && c.NodeId == caller.Nodes[1].Id
        );
        preview.IncompatibleCallers.ShouldContain(c => c.SubflowId == nested.SubflowId);
        var details = await service.DescribeCallersAsync(
            new(f.HostId),
            preview.IncompatibleCallers,
            CancellationToken.None
        );
        details.Single(d => d.Caller.FlowId == flow).Invocation!.Interface.Inputs.ShouldBe([port]);
        details
            .Single(d => d.Caller.SubflowId == nested.SubflowId)
            .Invocation!.Interface.Inputs.ShouldBe([renamed]);
        foreach (var detail in details)
        {
            var delta = AutomationSubflowDefinitions.Changes(
                detail.Invocation!.Interface,
                preview.Candidate.Interface
            );
            delta
                .Single(d => d.Id == port.Id)
                .Previous.ShouldBe(detail.Invocation.Interface.Inputs[0]);
            delta.ShouldAllBe(d => !d.Compatible);
            delta.Single(d => d.Id == added.Id).Previous.ShouldBeNull();
        }
        var published = (
            await service.PublishAsync(structural, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Published>();
        published.PreviousInterface!.Inputs.ShouldBe([renamed]);
        published.IncompatibleCallers.ShouldBe(preview.IncompatibleCallers);
        await using var db = await f.Database.CreateDbContextAsync();
        var saved = await db.AutomationFlowNodes.SingleAsync(n => n.Id == caller.Nodes[1].Id.Value);
        AutomationSubflowDefinitions
            .TryRead(
                new(
                    saved.DefinitionId,
                    saved.DefinitionSchemaVersion,
                    System.Text.Json.JsonDocument.Parse(saved.ConfigurationJson).RootElement
                ),
                out var authored
            )
            .ShouldBeTrue();
        authored.Interface.Inputs.ShouldBe([port]);
        var otherHost = await f.SeedHostAsync(
            "other-metadata",
            BlokeBot.Persistence.Models.HostFeatureFlags.Automations
        );
        (
            await service.DescribeCallersAsync(
                new(otherHost),
                preview.IncompatibleCallers,
                CancellationToken.None
            )
        ).ShouldBeEmpty();
        var replaced = changed with { Id = new("new-id") };
        var next = (
            await service.PreviewAsync(
                Subflow(f.HostId, new([replaced, added], [])) with
                {
                    Id = first.SubflowId,
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        var changes = AutomationSubflowDefinitions.Changes(
            next.PreviousInterface,
            next.Candidate.Interface
        );
        changes.Single(d => d.Id == port.Id).Current.ShouldBeNull();
        changes.Single(d => d.Id == replaced.Id).Previous.ShouldBeNull();
    }
}
