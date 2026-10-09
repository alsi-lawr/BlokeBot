namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _transport = """
          const loadCurrentState = async (signal) => {
            root.dataset.status = "loading";
            canvas.replaceChildren();
            clearCues();
            const response = await fetch(root.dataset.stateUrl, {
              cache: "no-store",
              credentials,
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

            if (
              loadedSnapshotSequence !== null &&
              snapshot.sequence !== loadedSnapshotSequence &&
              !refreshAppearanceStylesheet(snapshot.sequence)
            ) {
              throw new Error("Overlay appearance is invalid.");
            }
            loadedSnapshotSequence = snapshot.sequence;
            root.dataset.snapshotSequence = String(snapshot.sequence);
            root.dataset.status = "ready";
          };

          const consumeLiveStream = async (signal) => {
            const response = await fetch(root.dataset.liveUrl, {
              cache: "no-store",
              credentials,
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
                clearCues();
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
              if (envelope.eventType === "cue") {
                if (!renderCue(envelope.payload)) {
                  return "resync";
                }
                liveSequence = envelope.sequence;
                return "continue";
              }
              if (envelope.eventType === "cueStop") {
                if (!stopCue(envelope.runId)) {
                  return "resync";
                }
                liveSequence = envelope.sequence;
                return "continue";
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
              if (envelope.eventType === "test") {
                showTestPulse();
              }
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
                if (!liveEnabled) {
                  root.dataset.status = "representative";
                  return;
                }
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

          const dispose = () => {
            pageLifetime.abort();
            window.clearTimeout(testPulseTimer);
            window.clearTimeout(presentationAnimationTimer);
            window.clearTimeout(giveawayCountdownTimer);
            window.clearTimeout(progressRotationTimer);
            clearCues();
          };
          window.addEventListener("pagehide", dispose, { once: true, signal: pageLifetime.signal });
          if (options.startTransport !== false) void run();
          return { applyPresentation, renderCue, stopCue, dispose };
          };
          window.blokeBotOverlayRenderer = { create };
          const root = document.getElementById("overlay-root");
          if (root) create(root, document.getElementById("overlay-canvas"),
            document.getElementById("cue-canvas"), document.getElementById("overlay-appearance-style"));
        })();
        """;
}
