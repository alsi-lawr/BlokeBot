using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayEditorPage
{
    private IReadOnlyList<OverlayMediaAssetView> _mediaAssets = [];
    private string _configuration = "";
    private bool _configurationOpen;

    private async Task LoadMediaAsync()
    {
        var result = await _media.ListAssetsAsync(PageContext.Session, _lifetime.Token);
        if (result is OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Succeeded found)
        {
            _mediaAssets = found.Value;
        }
        else if (result is OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Rejected rejected)
        {
            _message = rejected.Reason.Message;
        }
    }

    private void ConfigurationInput(ChangeEventArgs args) =>
        _configuration = args.Value?.ToString() ?? "";

    private async Task ApplyConfigurationAsync(FullOverlayEditorView view)
    {
        try
        {
            using var json = JsonDocument.Parse(_configuration);
            await CommandAtAsync(
                new { kind = "configuration", value = json.RootElement.Clone() },
                view.Selected,
                view.Revision
            );
        }
        catch (JsonException)
        {
            _message =
                "Configuration JSON is incomplete. Its text is kept here; apply it when ready.";
        }
    }

    private IEnumerable<OverlayMediaAssetView> MediaFor(string kind) =>
        _mediaAssets.Where(asset =>
            asset.ContentType.StartsWith($"{kind}/", StringComparison.Ordinal)
        );

    private static string MediaAsset(FullOverlayWidget widget) =>
        widget.Configuration.ValueKind == JsonValueKind.Object
        && widget.Configuration.TryGetProperty("assetId", out var value)
            ? value.GetString() ?? ""
            : "";

    private async Task SetAssetAsync(Guid id, FullOverlayEditorView view)
    {
        if (view.Widget is not { } widget || widget.Configuration.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var configuration = JsonNode.Parse(widget.Configuration.GetRawText())!.AsObject();
        configuration["assetId"] = id;
        await CommandAtAsync(
            new
            {
                kind = "configuration",
                value = JsonSerializer.SerializeToElement(configuration),
            },
            view.Selected,
            view.Revision
        );
        _configuration = _view.Widget?.Configuration.GetRawText() ?? "";
    }

    private Task MediaSelectedAsync(ChangeEventArgs args, FullOverlayEditorView view) =>
        Guid.TryParse(args.Value?.ToString(), out var id)
            ? SetAssetAsync(id, view)
            : Task.CompletedTask;

    private async Task UploadAsync(InputFileChangeEventArgs args, FullOverlayEditorView view)
    {
        var selection = view.Selected;
        var revision = view.Revision;
        var file = args.File;
        await using var stream = file.OpenReadStream(file.Size);
        var result = await _media.UploadAssetAsync(
            PageContext.Session,
            file.Name,
            file.ContentType,
            stream,
            _lifetime.Token
        );
        if (result is OverlayCueResult<OverlayMediaAssetView>.Succeeded uploaded)
        {
            await LoadMediaAsync();
            if (_view.Selected == selection && _view.Revision == revision)
            {
                await SetAssetAsync(uploaded.Value.Id, view);
            }
            _message = "Media uploaded.";
        }
        else if (result is OverlayCueResult<OverlayMediaAssetView>.Rejected rejected)
        {
            _message = rejected.Reason.Message;
        }
    }
}
