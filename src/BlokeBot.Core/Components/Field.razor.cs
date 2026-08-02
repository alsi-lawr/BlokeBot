using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components;

public partial class Field
{
    private ElementReference _input;

    [Parameter]
    public required string Id { get; set; }

    [Parameter]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public string? ErrorMessage { get; set; }

    [Parameter]
    public long FocusRequest { get; set; }

    [Parameter]
    public string Placeholder { get; set; } = string.Empty;

    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    private string _inputId => Id;

    private bool _hasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    private string? _errorId => _hasError ? $"{_inputId}-error" : null;

    public ValueTask FocusAsync() => _input.FocusAsync();

    private long _handledFocusRequest;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (FocusRequest <= _handledFocusRequest)
        {
            return;
        }

        _handledFocusRequest = FocusRequest;
        await FocusAsync();
    }

    private Task OnInput(ChangeEventArgs e) =>
        ValueChanged.InvokeAsync(e.Value?.ToString() ?? string.Empty);
}
