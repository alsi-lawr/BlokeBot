using System.Collections.Immutable;
using System.Text;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginHttpHostModule(PluginOutboundHttpClient http) : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Http;

    public ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<PluginHostCallOutcome>(Unavailable());

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginWorkerInvocationIdentity identity,
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        if (!TryRequest((PluginValue.Map)call.Arguments[0], out var request))
        {
            return Failed(PluginHostFailureCode.InvalidArguments, "HTTP request is invalid.");
        }

        var outcome = await http.SendAsync(identity.Plugin.PluginId, request, cancellationToken);
        return new PluginHostCallOutcome.Returned(ToValue(outcome));
    }

    private static bool TryRequest(PluginValue.Map value, out PluginHttpRequest request)
    {
        request = null!;
        var properties = value.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value,
            StringComparer.Ordinal
        );
        if (
            !properties.TryGetValue("method", out var methodValue)
            || methodValue is not PluginValue.String methodText
            || !TryMethod(methodText.Value, out var method)
            || !properties.TryGetValue("url", out var urlValue)
            || urlValue is not PluginValue.String urlText
            || !Uri.TryCreate(urlText.Value, UriKind.Absolute, out var uri)
        )
        {
            return false;
        }

        var headers = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        if (properties.TryGetValue("headers", out var headerValue))
        {
            if (headerValue is not PluginValue.Map headerMap)
            {
                return false;
            }
            foreach (var header in headerMap.Properties)
            {
                if (
                    header.Value is not PluginValue.String text
                    || !headers.TryAdd(header.Name, text.Value)
                )
                {
                    return false;
                }
            }
        }

        var body = ReadOnlyMemory<byte>.Empty;
        if (properties.TryGetValue("body", out var bodyValue))
        {
            if (bodyValue is not PluginValue.String text)
            {
                return false;
            }
            body = Encoding.UTF8.GetBytes(text.Value);
        }

        request = new(method, uri, headers.ToImmutable(), body);
        return true;
    }

    private static bool TryMethod(string value, out PluginHttpMethod method) =>
        Enum.TryParse(value, ignoreCase: true, out method) && Enum.IsDefined(method);

    private static PluginValue.Map ToValue(PluginHttpOutcome outcome) =>
        outcome switch
        {
            PluginHttpOutcome.Response response => Map(
                new("kind", new PluginValue.String("response")),
                new("status", new PluginValue.Number(response.StatusCode)),
                new(
                    "headers",
                    new PluginValue.Map(
                        response
                            .Headers.Select(header => new PluginValueProperty(
                                header.Key,
                                new PluginValue.String(header.Value)
                            ))
                            .ToImmutableArray()
                    )
                ),
                new(
                    "bodyBase64",
                    new PluginValue.String(Convert.ToBase64String(response.Body.Span))
                )
            ),
            PluginHttpOutcome.Rejected rejected => Map(
                new("kind", new PluginValue.String("rejected")),
                new("code", new PluginValue.String(Code(rejected.Code)))
            ),
            PluginHttpOutcome.Failed failed => Map(
                new("kind", new PluginValue.String("failed")),
                new("code", new PluginValue.String(Code(failed.Code)))
            ),
            _ => Map(new PluginValueProperty("kind", new PluginValue.String("failed"))),
        };

    private static PluginValue.Map Map(params PluginValueProperty[] properties) =>
        new([.. properties]);

    private static string Code<TCode>(TCode code)
        where TCode : struct, Enum => code.ToString().ToLowerInvariant();

    private static PluginHostCallOutcome.Failed Failed(
        PluginHostFailureCode code,
        string message
    ) => new(new(code, message));

    private static PluginHostCallOutcome.Failed Unavailable() =>
        Failed(PluginHostFailureCode.Unavailable, "Plugin HTTP is unavailable.");
}
