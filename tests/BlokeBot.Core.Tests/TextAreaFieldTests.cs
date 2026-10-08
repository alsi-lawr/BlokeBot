using BlokeBot.Core.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class TextAreaFieldTests
{
    [Test]
    [Arguments("normalized by parent")]
    [Arguments("original authoritative value")]
    public async Task AsynchronousParentDecisionAndExternalReplacement_RemainAuthoritative(
        string accepted
    )
    {
        using var context = new BunitContext();
        var parent = context.Render<DelayedParent>();

        var input = parent.Find("textarea").InputAsync(new() { Value = "  native draft  " });
        await parent.Instance.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        parent.Instance.Decision.SetResult(accepted);
        await input.WaitAsync(TimeSpan.FromSeconds(5));

        parent.Find("textarea").GetAttribute("value").ShouldBe(accepted);
        await parent.InvokeAsync(() => parent.Instance.Replace("external authoritative value"));
        parent.Find("textarea").GetAttribute("value").ShouldBe("external authoritative value");
    }

    [Test]
    public void ValidationFocus_AdvancesOnceWithoutBlockingExternalValueChanges()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var field = context.Render<TextAreaField>(parameters =>
            parameters
                .Add(component => component.Id, "message")
                .Add(component => component.Value, "original")
                .Add(component => component.FocusRequest, 1L)
        );
        context
            .JSInterop.Invocations.Single()
            .Arguments[0]
            .ShouldBeElementReferenceTo(field.Find("textarea"));

        field.Render(parameters =>
            parameters
                .Add(component => component.Value, "replacement")
                .Add(component => component.FocusRequest, 1L)
        );

        field.Find("textarea").GetAttribute("value").ShouldBe("replacement");
        context.JSInterop.Invocations.Count.ShouldBe(1);
        field.Render(parameters => parameters.Add(component => component.FocusRequest, 2L));
        field.Render(parameters => parameters.Add(component => component.FocusRequest, 1L));
        context.JSInterop.Invocations.Count.ShouldBe(2);
    }

    private sealed class DelayedParent : ComponentBase
    {
        private string _value = "original authoritative value";

        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<string> Decision { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Replace(string value)
        {
            _value = value;
            StateHasChanged();
        }

        private async Task AcceptAsync(string _)
        {
            Entered.SetResult();
            _value = await Decision.Task;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TextAreaField>(0);
            builder.AddComponentParameter(1, nameof(TextAreaField.Id), "message");
            builder.AddComponentParameter(2, nameof(TextAreaField.Value), _value);
            builder.AddComponentParameter(
                3,
                nameof(TextAreaField.ValueChanged),
                EventCallback.Factory.Create<string>(this, AcceptAsync)
            );
            builder.CloseComponent();
        }
    }
}
