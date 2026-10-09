using System.Globalization;
using System.Text.Json;
using BlokeBot.Plugins.Contracts;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayWidgetSettings
{
    [Parameter, EditorRequired]
    public required FullOverlayWidget Widget { get; set; }

    [Parameter]
    public IReadOnlyList<FullOverlayBindingChoice> Bindings { get; set; } = [];

    [Parameter]
    public IReadOnlyList<PluginWidgetConfigurationField> PluginFields { get; set; } = [];

    [Parameter]
    public EventCallback<object> Command { get; set; }

    private JsonElement? Value(params string[] path)
    {
        var value = Widget.Configuration;
        foreach (var part in path)
        {
            if (
                value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty(part, out var child)
            )
            {
                return null;
            }
            value = child;
        }
        return value;
    }

    private string Text(params string[] path) => Value(path)?.ToString() ?? "";

    private bool Boolean(params string[] path) => Value(path)?.ValueKind == JsonValueKind.True;

    private string[]? AppearancePath() =>
        Value("appearance") is { ValueKind: JsonValueKind.Object } ? ["appearance"]
        : Value("feed", "appearance") is { ValueKind: JsonValueKind.Object }
            ? ["feed", "appearance"]
        : null;

    private Task ChangeAsync(string[] path, object? value) =>
        Command.InvokeAsync(
            new
            {
                kind = "configuration-field",
                path,
                value,
            }
        );

    private Task NumberAsync(string[] path, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number)
            ? ChangeAsync(path, number)
            : Task.CompletedTask;

    private Task BindingAsync(string property, ChangeEventArgs args) =>
        property == "queueId"
            ? NumberAsync([property], args.Value?.ToString() ?? "0")
            : ChangeAsync(
                [property],
                string.IsNullOrEmpty(args.Value?.ToString()) ? null : args.Value?.ToString()
            );
}
