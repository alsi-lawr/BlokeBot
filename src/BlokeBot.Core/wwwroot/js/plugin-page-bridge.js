const protocol = "blokebot.plugin-page";
const version = 1;

export function initializePluginPageBridge(
  iframe,
  dotnet,
  sessionId,
  messageOrigins,
  maximumBytes,
) {
  const origins = new Set(messageOrigins);
  const encoder = new TextEncoder();
  let disposed = false;

  const onMessage = async (event) => {
    if (
      disposed ||
      event.source !== iframe.contentWindow ||
      !origins.has(event.origin) ||
      event.data === null ||
      typeof event.data !== "object" ||
      event.data.protocol !== protocol ||
      event.data.version !== version ||
      event.data.sessionId !== sessionId ||
      typeof event.data.messageId !== "string" ||
      (event.data.kind !== "action" && event.data.kind !== "navigate")
    ) {
      return;
    }

    let json;
    try {
      json = JSON.stringify(event.data);
    } catch {
      return;
    }
    if (encoder.encode(json).byteLength > maximumBytes) {
      return;
    }

    const response = await dotnet.invokeMethodAsync(
      "ReceiveEmbeddedMessageAsync",
      event.origin,
      json,
    );
    if (disposed || event.source !== iframe.contentWindow) {
      return;
    }
    event.source.postMessage(
      {
        protocol,
        version,
        sessionId,
        messageId: event.data.messageId,
        kind: "result",
        accepted: response.accepted,
        message: response.message,
      },
      event.origin,
    );
    if (
      response.accepted &&
      typeof response.navigationUrl === "string" &&
      response.navigationUrl.startsWith("https://")
    ) {
      iframe.src = response.navigationUrl;
    }
  };

  const onLoad = () => {
    for (const origin of origins) {
      iframe.contentWindow?.postMessage(
        { protocol, version, sessionId, kind: "host-ready" },
        origin,
      );
    }
  };

  window.addEventListener("message", onMessage);
  iframe.addEventListener("load", onLoad);
  onLoad();

  return {
    dispose() {
      disposed = true;
      window.removeEventListener("message", onMessage);
      iframe.removeEventListener("load", onLoad);
    },
  };
}
