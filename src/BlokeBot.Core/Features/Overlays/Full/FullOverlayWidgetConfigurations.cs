using System.Text.Json;

namespace BlokeBot.Core.Features.Overlays.Full;

internal static class FullOverlayWidgetConfigurations
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static T? Read<T>(JsonElement value)
        where T : class
    {
        try
        {
            return value.Deserialize<T>(_json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
