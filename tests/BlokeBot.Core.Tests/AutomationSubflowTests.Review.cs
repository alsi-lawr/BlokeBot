using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task SubflowReview_ChildAndPublishedBaselinesRefuseWithoutPartialWritesThenExplicitReviewSucceeds()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var service = Subflows(f);
        var childDraft = Subflow(f.HostId);
        var child = await Publish(service, childDraft);
        var parentDraft = Nesting(Subflow(f.HostId), child);
        var reviewed = await ReviewSubflowAsync(service, parentDraft);
        _ = await Publish(service, childDraft with { Description = "New child revision" });
        var before = await PublicationStateAsync(f);
        var stale = (
            await service.PublishAsync(parentDraft, reviewed, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        (await PublicationStateAsync(f)).ShouldBe(before);
        stale.Refreshed.Reviewed.Dependencies[child.SubflowId].Revision.ShouldBe(2);
        var parent = await Publish(service, parentDraft);
        var prior = await ReviewSubflowAsync(service, parentDraft);
        _ = await Publish(
            service,
            parentDraft with
            {
                Description = "Concurrent published baseline",
            }
        );
        before = await PublicationStateAsync(f);
        _ = (
            await service.PublishAsync(parentDraft, prior, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        (await PublicationStateAsync(f)).ShouldBe(before);
        var renewed = await Publish(service, parentDraft);
        renewed.Revision.ShouldBe(parent.Revision + 2);
        before = await PublicationStateAsync(f);
        _ = (
            await service.PublishAsync(parentDraft, prior, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        (await PublicationStateAsync(f)).ShouldBe(before);
    }

    [Test]
    public async Task SubflowReview_FullCallerAuthoredImpactAndSameCountIdentityChangesAreNotAPagedCount()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var service = Subflows(f);
        var text = SubflowPort("value", AutomationPortValueType.Text);
        var original = Subflow(f.HostId, new([text], []));
        var first = await Publish(service, original);
        var callers = new Dictionary<AutomationFlowId, AutomationFlowDraft>();
        for (var i = 0; i < AutomationSubflowService.LibraryPageSize + 1; i++)
        {
            var caller = Caller(
                f.HostId,
                first,
                ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                    text.Id,
                    new AutomationValue.Text("Keep me")
                )
            );
            var saved = (
                await f.Flows.SaveAsync(caller, default)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            callers.Add(saved.FlowId, caller with { Id = saved.FlowId });
        }
        var nestedDraft = Nesting(Subflow(f.HostId), first);
        nestedDraft = nestedDraft with
        {
            Graph = nestedDraft.Graph with
            {
                Nodes = nestedDraft.Graph.Nodes.SetItem(
                    1,
                    Invoke(
                        first,
                        nestedDraft.Graph.Nodes[1].Id,
                        ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                            text.Id,
                            new AutomationValue.Text("Nested value")
                        )
                    )
                ),
            },
        };
        var nested = await Publish(service, nestedDraft);
        var numberDraft = Subflow(
            f.HostId,
            new([text with { ValueType = AutomationPortValueType.Number }], [])
        ) with
        {
            Id = original.Id,
        };
        var number = await Publish(service, numberDraft);
        var proposed = Subflow(
            f.HostId,
            new([text with { ValueType = AutomationPortValueType.Boolean }], [])
        ) with
        {
            Id = original.Id,
        };
        var preview = (
            await service.PreviewAsync(proposed, default)
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        var outsidePage = preview
            .IncompatibleCallers.Where(c => c.FlowId is not null)
            .ElementAt(AutomationSubflowService.LibraryPageSize);
        (
            await service.DescribeCallersAsync(new(f.HostId), preview.IncompatibleCallers, default)
        ).ShouldNotContain(d => d.Caller == outsidePage);
        var callerDraft = callers[outsidePage.FlowId!.Value];
        var repaired = callerDraft with
        {
            Nodes = callerDraft.Nodes.SetItem(
                1,
                Invoke(
                    number,
                    outsidePage.NodeId,
                    ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                        text.Id,
                        new AutomationValue.Number(1)
                    )
                )
            ),
        };
        _ = (
            await f.Flows.SaveAsync(repaired, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var before = await PublicationStateAsync(f);
        var stale = (
            await service.PublishAsync(proposed, preview.Reviewed, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        stale.Refreshed.IncompatibleCallers.ShouldBe(
            preview.IncompatibleCallers,
            ignoreOrder: true
        );
        stale
            .Refreshed.Reviewed.Callers.Single(c => c.Caller == outsidePage)
            .Interface!.Inputs[0]
            .ValueType.ShouldBe(AutomationPortValueType.Number);
        (await PublicationStateAsync(f)).ShouldBe(before);
        var nestedReview = await ReviewSubflowAsync(service, proposed);
        var newNested = nestedDraft with
        {
            Graph = nestedDraft.Graph with
            {
                Nodes = nestedDraft.Graph.Nodes.SetItem(
                    1,
                    Invoke(
                        number,
                        nestedDraft.Graph.Nodes[1].Id,
                        ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                            text.Id,
                            new AutomationValue.Number(2)
                        )
                    )
                ),
            },
        };
        _ = await Publish(service, newNested);
        before = await PublicationStateAsync(f);
        stale = (
            await service.PublishAsync(proposed, nestedReview, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        stale
            .Refreshed.Reviewed.Callers.Single(c => c.Caller.SubflowId == nested.SubflowId)
            .Interface!.Inputs[0]
            .ValueType.ShouldBe(AutomationPortValueType.Number);
        (await PublicationStateAsync(f)).ShouldBe(before);
        var identityReview = await ReviewSubflowAsync(service, proposed);
        _ = await f.Flows.DeleteAsync(new(f.HostId), outsidePage.FlowId.Value, default);
        _ = (
            await f.Flows.SaveAsync(
                Caller(
                    f.HostId,
                    number,
                    ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                        text.Id,
                        new AutomationValue.Number(3)
                    )
                ),
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        before = await PublicationStateAsync(f);
        stale = (
            await service.PublishAsync(proposed, identityReview, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        stale.Refreshed.IncompatibleCallers.Length.ShouldBe(identityReview.Callers.Length);
        stale.Refreshed.IncompatibleCallers.ShouldNotContain(outsidePage);
        (await PublicationStateAsync(f)).ShouldBe(before);
        var boolean = await Publish(service, proposed);
        var compatibilityReview = await ReviewSubflowAsync(service, proposed);
        var remaining = callers.First(pair => pair.Key != outsidePage.FlowId.Value);
        var oldCall = remaining.Value.Nodes[1];
        _ = (
            await f.Flows.SaveAsync(
                remaining.Value with
                {
                    Nodes = remaining.Value.Nodes.SetItem(
                        1,
                        Invoke(
                            boolean,
                            oldCall.Id,
                            ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                                text.Id,
                                new AutomationValue.Boolean(true)
                            )
                        )
                    ),
                },
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        before = await PublicationStateAsync(f);
        stale = (
            await service.PublishAsync(proposed, compatibilityReview, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        stale.Refreshed.IncompatibleCallers.Length.ShouldBe(compatibilityReview.Callers.Length - 1);
        stale.Refreshed.IncompatibleCallers.ShouldNotContain(c =>
            c.FlowId == remaining.Key && c.NodeId == oldCall.Id
        );
        (await PublicationStateAsync(f)).ShouldBe(before);
        _ = await Publish(service, proposed);
    }

    [Test]
    public async Task SubflowReview_UnrelatedMetadataAndRegeneratedCandidateIdentityDoNotInvalidateButExecutionAndValidationDo()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var service = Subflows(f);
        var draft = Subflow(f.HostId);
        var first = await Publish(service, draft);
        var caller = Caller(f.HostId, first);
        var saved = (
            await f.Flows.SaveAsync(caller, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var review = await ReviewSubflowAsync(service, draft);
        _ = await f.Flows.SaveAsync(
            caller with
            {
                Id = saved.FlowId,
                Name = "Unrelated caller name",
                Nodes =
                [
                    .. caller.Nodes.Select(n =>
                        n with
                        {
                            Position = new(new(900), new(800)),
                            DisplayAlias = "Visual alias",
                        }
                    ),
                ],
            },
            default
        );
        var other = await f.SeedHostAsync("other-review", HostFeatureFlags.Automations);
        _ = await Publish(service, Subflow(other));
        f.Clock.Advance(TimeSpan.FromMinutes(2));
        var moved = draft with
        {
            Graph = draft.Graph with
            {
                Name = "Visual name",
                Nodes =
                [
                    .. draft.Graph.Nodes.Select(n =>
                        n with
                        {
                            Position = new(new(400), new(500)),
                            DisplayAlias = "Visual metadata",
                        }
                    ),
                ],
            },
        };
        _ = (
            await service.PublishAsync(moved, review, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Published>();
        review = await ReviewSubflowAsync(service, draft);
        var changed = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = draft.Graph.Nodes.SetItem(
                    0,
                    draft.Graph.Nodes[0] with
                    {
                        FailurePolicy = AutomationNodeFailurePolicy.Continue,
                    }
                ),
            },
        };
        var before = await PublicationStateAsync(f);
        _ = (
            await service.PublishAsync(changed, review, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        (await PublicationStateAsync(f)).ShouldBe(before);
        _ = await f.Features.DisableAsync(f.HostId, HostFeatureFlags.Automations, default);
        _ = (
            await service.PublishAsync(draft, review, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>();
        (await PublicationStateAsync(f)).ShouldBe(before);
    }

    [Test]
    public async Task SubflowReview_ApplyRechecksAfterAConcurrentPublisherBeforeItsHostLock()
    {
        var window = new ReviewApplyWindow();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [window]);
        var service = Subflows(f);
        var draft = Subflow(f.HostId);
        _ = await Publish(service, draft);
        var reviewed = await ReviewSubflowAsync(service, draft);
        window.Armed = true;
        var applying = service.PublishAsync(draft, reviewed, default);
        await window.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            _ = await Publish(service, draft with { Description = "Concurrent publication" });
        }
        finally
        {
            _ = window.Release.TrySetResult();
        }
        var before = await PublicationStateAsync(f);
        _ = (
            await applying.WaitAsync(TimeSpan.FromSeconds(20))
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        (await PublicationStateAsync(f)).ShouldBe(before);
    }

    private static async Task<string> PublicationStateAsync(RuntimeFixture f)
    {
        await using var db = await f.Database.CreateDbContextAsync();
        return JsonSerializer.Serialize(
            new
            {
                Current = await db
                    .AutomationSubflows.OrderBy(r => r.HostId)
                    .ThenBy(r => r.Id)
                    .Select(r => new
                    {
                        r.HostId,
                        r.Id,
                        r.LastRevision,
                    })
                    .ToArrayAsync(),
                Revisions = await db
                    .AutomationSubflowRevisions.OrderBy(r => r.Id)
                    .Select(r => new { r.Id, r.SnapshotJson })
                    .ToArrayAsync(),
                Nested = await db
                    .AutomationSubflowNestedCallers.OrderBy(r => r.CallerSubflowId)
                    .ThenBy(r => r.NodeId)
                    .Select(r => new
                    {
                        r.CallerSubflowId,
                        r.NodeId,
                        r.SubflowId,
                    })
                    .ToArrayAsync(),
                Callers = await db
                    .AutomationSubflowCallers.OrderBy(r => r.NodeId)
                    .Select(r => new { r.NodeId, r.SubflowId })
                    .ToArrayAsync(),
                Nodes = await db
                    .AutomationFlowNodes.OrderBy(r => r.Id)
                    .Select(r => new { r.Id, r.ConfigurationJson })
                    .ToArrayAsync(),
            }
        );
    }

    private sealed class ReviewApplyWindow : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && command.CommandText.Contains("FROM \"hosts\"", StringComparison.Ordinal))
            {
                Armed = false;
                _ = Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
