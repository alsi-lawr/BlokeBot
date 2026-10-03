using System.Text.Json;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayEditorPage
{
    private static readonly JsonSerializerOptions _candidateJson = new(JsonSerializerDefaults.Web);

    private async Task<FullOverlayDocument> ReadCandidateAsync()
    {
        await using var reference = await _client!.InvokeAsync<IJSStreamReference>(
            "candidateStream",
            _lifetime.Token
        );
        // This is the frozen JSON blob's measured length, not a document-size policy.
        await using var stream = await reference.OpenReadStreamAsync(
            reference.Length,
            _lifetime.Token
        );
        return (
            await JsonSerializer.DeserializeAsync<FullOverlayDocument>(
                stream,
                _candidateJson,
                _lifetime.Token
            )
        )!;
    }
}
