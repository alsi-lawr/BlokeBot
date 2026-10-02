namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _runtime = """
        (() => {
          "use strict";

          const create = (root, canvas, cueCanvas, appearanceStylesheet, options = {}) => {
          if (
            !(root instanceof HTMLElement) ||
            !(canvas instanceof SVGSVGElement) ||
            !(cueCanvas instanceof HTMLElement) ||
            !(appearanceStylesheet instanceof HTMLLinkElement)
          ) {
            return;
          }

          const initialRetryDelayMilliseconds = 500;
          const maximumRetryDelayMilliseconds = 30000;
          const jitterMinimum = 0.75;
          const jitterRange = 0.5;
          const pageLifetime = new AbortController();
          const credentials = options.credentials ??
            (root.dataset.credentials === "same-origin" ? "same-origin" : "omit");
          const clipPrefix = options.clipPrefix ?? "";
          const liveEnabled = root.dataset.liveEnabled !== "false";
          let testPulseTimer = null;
          let presentationAnimationTimer = null;
          let giveawayCountdownTimer = null;
          let progressRotationTimer = null;
          let loadedSnapshotSequence = null;
          const cueTimers = new Map();
          const svgNamespace = canvas.namespaceURI;

          const refreshAppearanceStylesheet = (sequence) => {
            if (!Number.isSafeInteger(sequence) || sequence < 1) {
              return false;
            }
            const url = new URL(appearanceStylesheet.href, window.location.href);
            if (url.origin !== window.location.origin) {
              return false;
            }
            url.searchParams.set("revision", String(sequence));
            appearanceStylesheet.href = url.href;
            return true;
          };

          const showTestPulse = () => {
            root.dataset.testPulse = "active";
            if (testPulseTimer !== null) {
              window.clearTimeout(testPulseTimer);
            }
            testPulseTimer = window.setTimeout(() => {
              delete root.dataset.testPulse;
              testPulseTimer = null;
            }, 1500);
          };

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
        """;
}
