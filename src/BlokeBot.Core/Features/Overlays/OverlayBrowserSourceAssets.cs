namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayBrowserSourceAssets
{
    internal const string Stylesheet = """
        :root {
          background: transparent;
          color-scheme: only light;
        }

        html,
        body {
          width: 100%;
          height: 100%;
          margin: 0;
          overflow: hidden;
          background: transparent !important;
        }

        #overlay-root {
          position: fixed;
          inset: 0;
          overflow: hidden;
          background: transparent;
        }

        #overlay-canvas {
          display: block;
          width: 100%;
          height: 100%;
          overflow: hidden;
          background: transparent;
        }
        """;

    internal const string JavaScript = """
        (() => {
          "use strict";

          const root = document.getElementById("overlay-root");
          const canvas = document.getElementById("overlay-canvas");
          if (!(root instanceof HTMLElement) || !(canvas instanceof SVGSVGElement)) {
            return;
          }

          const initialRetryDelayMilliseconds = 500;
          const maximumRetryDelayMilliseconds = 30000;
          const jitterMinimum = 0.75;
          const jitterRange = 0.5;
          const pageLifetime = new AbortController();

          const delay = (milliseconds, signal) =>
            new Promise((resolve) => {
              let settled = false;
              const finish = () => {
                if (settled) {
                  return;
                }

                settled = true;
                signal.removeEventListener("abort", abort);
                resolve();
              };
              const timer = window.setTimeout(finish, milliseconds);
              const abort = () => {
                window.clearTimeout(timer);
                finish();
              };
              signal.addEventListener("abort", abort, { once: true });
              if (signal.aborted) {
                abort();
              }
            });

          const reconnectDelay = (attempt, randomValue) => {
            const exponent = Math.min(Math.max(attempt, 0), 16);
            const capped = Math.min(
              maximumRetryDelayMilliseconds,
              initialRetryDelayMilliseconds * 2 ** exponent,
            );
            return Math.min(
              maximumRetryDelayMilliseconds,
              Math.round(capped * (jitterMinimum + randomValue * jitterRange)),
            );
          };

          const applyPresentation = (projection, sequence, epoch, occurredAtUtc) => {
            if (
              projection?.overlayType !== "empty" ||
              projection?.schemaVersion !== 1 ||
              typeof projection?.state !== "object" ||
              projection.state === null
            ) {
              return false;
            }

            root.dataset.overlayType = projection.overlayType;
            root.dataset.schemaVersion = String(projection.schemaVersion);
            root.dataset.serverEpoch = epoch;
            root.dataset.sequence = String(sequence);
            root.dataset.generatedAtUtc = occurredAtUtc;
            return true;
          };

          const loadCurrentState = async (signal) => {
            root.dataset.status = "loading";
            canvas.replaceChildren();
            const response = await fetch(root.dataset.stateUrl, {
              cache: "no-store",
              credentials: "omit",
              headers: { Accept: "application/json" },
              signal,
            });
            if (!response.ok) {
              throw new Error("Overlay state is unavailable.");
            }

            const snapshot = await response.json();
            if (
              !applyPresentation(
                snapshot,
                snapshot.sequence,
                snapshot.serverEpoch,
                snapshot.generatedAtUtc,
              )
            ) {
              throw new Error("Overlay state is invalid.");
            }

            root.dataset.snapshotSequence = String(snapshot.sequence);
            root.dataset.status = "ready";
          };

          const consumeLiveStream = async (signal) => {
            const response = await fetch(root.dataset.liveUrl, {
              cache: "no-store",
              credentials: "omit",
              headers: { Accept: "text/event-stream" },
              signal,
            });
            if (!response.ok || response.body === null) {
              throw new Error("Overlay live state is unavailable.");
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = "";
            let liveEpoch = null;
            let liveSequence = null;

            const applyEnvelope = (envelope) => {
              if (envelope?.protocolVersion !== 1) {
                return "resync";
              }
              if (envelope.eventType === "reauthenticate" || envelope.eventType === "resync") {
                return "resync";
              }
              if (envelope.eventType === "baseline") {
                if (liveEpoch !== null) {
                  return "resync";
                }
                if (
                  typeof envelope.serverEpoch !== "string" ||
                  !Number.isSafeInteger(envelope.sequence) ||
                  !applyPresentation(
                    envelope.payload,
                    envelope.sequence,
                    envelope.serverEpoch,
                    envelope.occurredAtUtc,
                  )
                ) {
                  return "resync";
                }

                liveEpoch = envelope.serverEpoch;
                liveSequence = envelope.sequence;
                root.dataset.status = "live";
                return "continue";
              }
              if (liveEpoch === null || liveSequence === null) {
                return "resync";
              }
              if (envelope.serverEpoch !== liveEpoch) {
                return "resync";
              }
              if (!Number.isSafeInteger(envelope.sequence)) {
                return "resync";
              }
              if (envelope.sequence <= liveSequence) {
                return "continue";
              }
              if (envelope.sequence !== liveSequence + 1) {
                return "resync";
              }
              if (envelope.eventType !== "state" && envelope.eventType !== "test") {
                return "resync";
              }
              if (
                !applyPresentation(
                  envelope.payload,
                  envelope.sequence,
                  envelope.serverEpoch,
                  envelope.occurredAtUtc,
                )
              ) {
                return "resync";
              }

              liveSequence = envelope.sequence;
              return "continue";
            };

            try {
              while (!signal.aborted) {
                const next = await reader.read();
                buffer += decoder.decode(next.value ?? new Uint8Array(), {
                  stream: !next.done,
                });
                let boundary = buffer.indexOf("\n\n");
                while (boundary >= 0) {
                  const eventBlock = buffer.slice(0, boundary);
                  buffer = buffer.slice(boundary + 2);
                  const data = eventBlock
                    .split("\n")
                    .filter((line) => line.startsWith("data:"))
                    .map((line) => line.slice(5).trimStart())
                    .join("\n");
                  if (data.length > 0) {
                    let envelope;
                    try {
                      envelope = JSON.parse(data);
                    } catch {
                      return "resync";
                    }
                    if (applyEnvelope(envelope) === "resync") {
                      return "resync";
                    }
                  }
                  boundary = buffer.indexOf("\n\n");
                }

                if (next.done) {
                  return "reconnect";
                }
              }

              return "stopped";
            } finally {
              await reader.cancel();
              reader.releaseLock();
            }
          };

          const run = async () => {
            let attempt = 0;
            while (!pageLifetime.signal.aborted) {
              try {
                await loadCurrentState(pageLifetime.signal);
                const outcome = await consumeLiveStream(pageLifetime.signal);
                if (outcome === "stopped") {
                  return;
                }
                if (outcome === "resync") {
                  root.dataset.status = "resyncing";
                  attempt = 0;
                } else if (root.dataset.status === "live") {
                  attempt = 0;
                }
              } catch {
                if (pageLifetime.signal.aborted) {
                  return;
                }
              }

              root.dataset.status = "reconnecting";
              root.dataset.reconnectAttempt = String(attempt + 1);
              const milliseconds = reconnectDelay(attempt, Math.random());
              attempt += 1;
              await delay(milliseconds, pageLifetime.signal);
            }
          };

          window.addEventListener("pagehide", () => pageLifetime.abort(), { once: true });
          void run();
        })();
        """;
}
