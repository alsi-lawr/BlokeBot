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

        #cue-canvas {
          position: absolute;
          inset: 0;
          overflow: hidden;
          pointer-events: none;
        }

        .cue-run,
        .cue-layer {
          position: absolute;
          inset: 0;
        }

        .cue-layer {
          border: 0;
          background: transparent;
        }

        #overlay-root[data-test-pulse="active"] #overlay-canvas {
          animation: blokebot-overlay-test-pulse 1.5s ease-out;
          box-shadow: inset 0 0 0 24px rgba(59, 130, 246, 0);
        }

        .guessing-card,
        .giveaway-card,
        .event-feed-card {
          fill: rgba(15, 23, 42, 0.94);
          stroke: rgba(148, 163, 184, 0.72);
          stroke-width: 2;
        }

        .guessing-accent,
        .giveaway-accent,
        .event-feed-accent {
          fill: #60a5fa;
        }

        .guessing-kicker,
        .guessing-title,
        .guessing-detail,
        .guessing-result {
          fill: #f8fafc;
          font-family: ui-sans-serif, system-ui, sans-serif;
        }

        .giveaway-kicker,
        .giveaway-title,
        .giveaway-detail,
        .giveaway-result {
          fill: #f8fafc;
          font-family: ui-sans-serif, system-ui, sans-serif;
        }

        .event-feed-kicker,
        .event-feed-title {
          fill: #f8fafc;
          font-family: ui-sans-serif, system-ui, sans-serif;
        }
        .event-feed-kicker { fill: #93c5fd; font-size: 28px; font-weight: 800; letter-spacing: 4px; }
        .event-feed-title { font-size: 48px; font-weight: 800; }
        .event-feed-body-host { overflow: visible; }
        .event-feed-body {
          box-sizing: border-box;
          width: 100%;
          margin: 0;
          color: #cbd5e1;
          font-family: ui-sans-serif, system-ui, sans-serif;
          font-size: 32px;
          font-weight: 600;
          line-height: 44px;
          white-space: pre-wrap;
          overflow-wrap: anywhere;
        }
        #overlay-root[data-animation="card"] .event-feed-presentation,
        #overlay-root[data-animation="sample"] .event-feed-presentation { animation: event-feed-card 520ms cubic-bezier(0.22, 1, 0.36, 1); }
        @keyframes event-feed-card { from { opacity: 0; transform: translateX(-60px); } to { opacity: 1; transform: translateX(0); } }

        .giveaway-kicker {
          fill: #93c5fd;
          font-size: 30px;
          font-weight: 800;
          letter-spacing: 4px;
        }

        .giveaway-title {
          font-size: 58px;
          font-weight: 800;
        }

        .giveaway-detail {
          fill: #cbd5e1;
          font-size: 30px;
          font-weight: 600;
        }

        .giveaway-result {
          fill: #fef08a;
          font-size: 40px;
          font-weight: 800;
        }

        #overlay-root[data-animation="winner"] .giveaway-presentation {
          animation: guessing-overlay-result 640ms cubic-bezier(0.34, 1.56, 0.64, 1);
        }

        .guessing-kicker {
          fill: #93c5fd;
          font-size: 30px;
          font-weight: 800;
          letter-spacing: 4px;
        }

        .guessing-title {
          font-size: 58px;
          font-weight: 800;
        }

        .guessing-detail {
          fill: #cbd5e1;
          font-size: 30px;
          font-weight: 600;
        }

        .guessing-result {
          fill: #fef08a;
          font-size: 40px;
          font-weight: 800;
        }

        #overlay-root[data-animation="entrance"] .guessing-presentation {
          animation: guessing-overlay-entrance 480ms cubic-bezier(0.22, 1, 0.36, 1);
        }

        #overlay-root[data-animation="statusChange"] .guessing-presentation {
          animation: guessing-overlay-status 360ms ease-out;
        }

        #overlay-root[data-animation="result"] .guessing-presentation {
          animation: guessing-overlay-result 640ms cubic-bezier(0.34, 1.56, 0.64, 1);
        }

        @keyframes guessing-overlay-entrance {
          from {
            opacity: 0;
            transform: translateY(48px);
          }
          to {
            opacity: 1;
            transform: translateY(0);
          }
        }

        @keyframes guessing-overlay-status {
          from {
            opacity: 0.45;
          }
          to {
            opacity: 1;
          }
        }

        @keyframes guessing-overlay-result {
          0% {
            opacity: 0;
            transform: scale(0.92);
          }
          70% {
            opacity: 1;
            transform: scale(1.02);
          }
          100% {
            transform: scale(1);
          }
        }

        @media (prefers-reduced-motion: reduce) {
          #overlay-root[data-animation] .guessing-presentation,
          #overlay-root[data-animation] .giveaway-presentation,
          #overlay-root[data-animation] .event-feed-presentation,
          #overlay-root[data-test-pulse="active"] #overlay-canvas {
            animation: none;
          }
        }

        @keyframes blokebot-overlay-test-pulse {
          0% {
            box-shadow: inset 0 0 0 24px rgba(59, 130, 246, 0.95);
          }
          100% {
            box-shadow: inset 0 0 0 24px rgba(59, 130, 246, 0);
          }
        }
        """;

    internal const string JavaScript = """
        (() => {
          "use strict";

          const root = document.getElementById("overlay-root");
          const canvas = document.getElementById("overlay-canvas");
          const cueCanvas = document.getElementById("cue-canvas");
          const appearanceStylesheet = document.getElementById(
            "overlay-appearance-style",
          );
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
          const credentials =
            root.dataset.credentials === "same-origin" ? "same-origin" : "omit";
          const liveEnabled = root.dataset.liveEnabled !== "false";
          let testPulseTimer = null;
          let presentationAnimationTimer = null;
          let giveawayCountdownTimer = null;
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

          const svgElement = (name, attributes, text) => {
            const element = document.createElementNS(svgNamespace, name);
            for (const [attribute, value] of Object.entries(attributes)) {
              element.setAttribute(attribute, value);
            }
            if (typeof text === "string") {
              element.textContent = text;
            }
            return element;
          };

          const appendText = (group, className, x, y, text) => {
            const stableClass = className.endsWith("-kicker")
              ? " kicker"
              : className.endsWith("-title")
                ? " title"
                : className.endsWith("-result")
                  ? " result"
                  : " detail";
            group.append(
              svgElement(
                "text",
                { class: className + stableClass, x: String(x), y: String(y) },
                text,
              ),
            );
          };

          const createEventFeedBody = (text) => {
            const host = svgElement("foreignObject", {
              class: "event-feed-body-host",
              x: "-10000",
              y: "166",
              width: "1488",
              height: "10000",
              visibility: "hidden",
            });
            const body = document.createElement("div");
            body.setAttribute("class", "event-feed-body");
            body.textContent = text;
            host.append(body);
            return { host, body };
          };

          const validGuessingState = (state) => {
            if (typeof state !== "object" || state === null) {
              return false;
            }
            if (state.phase === "noRound") {
              return true;
            }
            if (
              (state.phase !== "open" &&
                state.phase !== "closed" &&
                state.phase !== "completed") ||
              typeof state.roundName !== "string" ||
              (state.guessCount !== null &&
                (!Number.isSafeInteger(state.guessCount) || state.guessCount < 0))
            ) {
              return false;
            }
            if (state.phase !== "completed") {
              return true;
            }
            return (
              typeof state.winningAnswer === "string" &&
              Array.isArray(state.winners) &&
              state.winners.every((winner) => typeof winner === "string") &&
              (state.awardedPointsPerWinner === null ||
                typeof state.awardedPointsPerWinner === "string") &&
              (state.pointLabel === null || typeof state.pointLabel === "string")
            );
          };

          const resultDetail = (state) => {
            const winners =
              state.winners.length === 0
                ? "No winning guesses"
                : state.winners.join(", ");
            if (
              state.winners.length === 0 ||
              state.awardedPointsPerWinner === null ||
              state.pointLabel === null
            ) {
              return winners;
            }
            return `${winners} · ${state.awardedPointsPerWinner} ${state.pointLabel} each`;
          };

          const validAppearance = (appearance) =>
            typeof appearance === "object" &&
            appearance !== null &&
            Number.isSafeInteger(appearance.x) &&
            Number.isSafeInteger(appearance.y) &&
            Number.isSafeInteger(appearance.width) &&
            Number.isSafeInteger(appearance.height) &&
            appearance.x >= 0 &&
            appearance.y >= 0 &&
            appearance.width >= 160 &&
            appearance.height >= 90 &&
            appearance.x + appearance.width <= 1920 &&
            appearance.y + appearance.height <= 1080;

          const renderGuessing = (state, appearance) => {
            canvas.replaceChildren();
            root.dataset.phase = state.phase;
            if (state.phase === "noRound") {
              return;
            }

            const geometryGroup = svgElement("g", {
              class: "overlay",
              transform: `translate(${appearance.x} ${appearance.y}) scale(${appearance.width / 1600} ${appearance.height / 270})`,
            });
            const presentationGroup = svgElement("g", {
              class: "guessing-presentation",
            });
            presentationGroup.append(
              svgElement("rect", {
                class: "guessing-card card",
                x: "0",
                y: "0",
                width: "1600",
                height: "270",
                rx: "30",
              }),
              svgElement("rect", {
                class: "guessing-accent accent",
                x: "0",
                y: "0",
                width: "16",
                height: "270",
                rx: "8",
              }),
            );

            const status =
              state.phase === "open"
                ? "GUESSING OPEN"
                : state.phase === "closed"
                  ? "ENTRIES CLOSED"
                  : "RESULT";
            appendText(presentationGroup, "guessing-kicker", 56, 62, status);
            appendText(presentationGroup, "guessing-title", 56, 135, state.roundName);
            if (state.phase === "completed") {
              appendText(
                presentationGroup,
                "guessing-result",
                56,
                202,
                `Winner: ${state.winningAnswer}`,
              );
              appendText(
                presentationGroup,
                "guessing-detail",
                760,
                202,
                resultDetail(state),
              );
            } else {
              const detail =
                state.guessCount === null
                  ? state.phase === "open"
                    ? "Send your guess in chat"
                    : "Waiting for the result"
                  : `${state.guessCount} ${state.guessCount === 1 ? "guess" : "guesses"}`;
              appendText(presentationGroup, "guessing-detail", 56, 205, detail);
            }
            geometryGroup.append(presentationGroup);
            canvas.append(geometryGroup);
          };

          const validGiveawayState = (state) => {
            if (
              typeof state !== "object" ||
              state === null ||
              typeof state.title !== "string" ||
              state.title.length < 1 ||
              state.title.length > 80
            ) {
              return false;
            }
            if (state.phase === "idle") {
              return true;
            }
            if (state.phase === "open") {
              return (
                (state.entrantCount === null ||
                  (Number.isSafeInteger(state.entrantCount) &&
                    state.entrantCount >= 0)) &&
                (state.closesAtUtc === null ||
                  !Number.isNaN(Date.parse(state.closesAtUtc))) &&
                (state.joinCommand === null ||
                  typeof state.joinCommand === "string")
              );
            }
            if (state.phase === "ending") {
              return (
                state.entrantCount === null ||
                (Number.isSafeInteger(state.entrantCount) &&
                  state.entrantCount >= 0)
              );
            }
            if (state.phase === "completed") {
              return (
                Array.isArray(state.winners) &&
                state.winners.every(
                  (winner) =>
                    typeof winner?.login === "string" &&
                    typeof winner?.awardedPoints === "string",
                ) &&
                typeof state.completedAtUtc === "string"
              );
            }
            return (
              state.phase === "cancelled" &&
              typeof state.message === "string" &&
              typeof state.completedAtUtc === "string"
            );
          };

          const giveawayDetail = (state) => {
            if (state.phase === "idle") {
              return "No giveaway is running";
            }
            if (state.phase === "ending") {
              return "Entries closed · choosing winners";
            }
            if (state.phase === "cancelled") {
              return state.message;
            }
            if (state.phase === "completed") {
              return state.winners.length === 0
                ? "Giveaway closed without a winner"
                : state.winners
                    .map(
                      (winner) =>
                        `${winner.login} · ${winner.awardedPoints} ${state.pointLabel ?? "points"}`,
                    )
                    .join("  •  ");
            }

            const details = [];
            if (state.entrantCount !== null) {
              details.push(
                `${state.entrantCount} ${
                  state.entrantCount === 1 ? "entrant" : "entrants"
                }`,
              );
            }
            if (state.closesAtUtc !== null) {
              const remaining = Math.max(
                0,
                Math.ceil((Date.parse(state.closesAtUtc) - Date.now()) / 1000),
              );
              details.push(
                remaining === 0
                  ? "Closing now"
                  : `${Math.floor(remaining / 60)}:${String(
                      remaining % 60,
                    ).padStart(2, "0")} remaining`,
              );
            }
            if (state.joinCommand !== null) {
              details.push(`Type ${state.joinCommand} to enter`);
            }
            return details.join("  •  ");
          };

          const renderGiveaway = (state, appearance) => {
            if (giveawayCountdownTimer !== null) {
              window.clearTimeout(giveawayCountdownTimer);
              giveawayCountdownTimer = null;
            }
            canvas.replaceChildren();
            root.dataset.phase = state.phase;
            if (state.phase === "idle") {
              return;
            }
            const geometryGroup = svgElement("g", {
              class: "overlay",
              transform: `translate(${appearance.x} ${appearance.y}) scale(${appearance.width / 1600} ${appearance.height / 270})`,
            });
            const presentationGroup = svgElement("g", {
              class: "giveaway-presentation",
            });
            presentationGroup.append(
              svgElement("rect", {
                class: "giveaway-card card",
                x: "0",
                y: "0",
                width: "1600",
                height: "270",
                rx: "30",
              }),
              svgElement("rect", {
                class: "giveaway-accent accent",
                x: "0",
                y: "0",
                width: "16",
                height: "270",
                rx: "8",
              }),
            );
            const status =
              state.phase === "open"
                ? "GIVEAWAY OPEN"
                : state.phase === "completed"
                  ? "WINNERS"
                  : state.phase === "ending"
                    ? "GIVEAWAY ENDING"
                    : state.phase === "cancelled"
                      ? "GIVEAWAY CLOSED"
                      : "GIVEAWAY";
            appendText(presentationGroup, "giveaway-kicker", 56, 62, status);
            appendText(presentationGroup, "giveaway-title", 56, 135, state.title);
            appendText(
              presentationGroup,
              state.phase === "completed"
                ? "giveaway-result"
                : "giveaway-detail",
              56,
              205,
              giveawayDetail(state),
            );
            geometryGroup.append(presentationGroup);
            canvas.append(geometryGroup);
            if (state.phase === "open" && state.closesAtUtc !== null) {
              giveawayCountdownTimer = window.setTimeout(
                () => renderGiveaway(state, appearance),
                1000,
              );
            }
          };

          const validEventFeedState = (state) => {
            if (typeof state !== "object" || state === null || !Array.isArray(state.pending)) return false;
            const validCard = (card) =>
              typeof card === "object" &&
              card !== null &&
              Number.isSafeInteger(card.id) &&
              card.id >= 0 &&
              (card.kind === "pointAward" ||
                card.kind === "guessingWinner" ||
                card.kind === "giveawayWinner") &&
              (card.priority === "normal" || card.priority === "high") &&
              typeof card.title === "string" &&
              typeof card.body === "string" &&
              typeof card.enqueuedAtUtc === "string" &&
              (card.displayDeadlineUtc === null ||
                typeof card.displayDeadlineUtc === "string");
            return (
              (state.active === null || validCard(state.active)) &&
              state.pending.every(validCard)
            );
          };

          const renderEventFeed = (state, appearance) => {
            canvas.replaceChildren();
            const card = state.active;
            if (card === null) return;
            const eventFeedBody = createEventFeedBody(card.body);
            canvas.append(eventFeedBody.host);
            const bodyHeight = Math.max(44, Math.ceil(eventFeedBody.body.scrollHeight));
            eventFeedBody.host.remove();
            eventFeedBody.host.setAttribute("x", "56");
            eventFeedBody.host.setAttribute("height", String(bodyHeight));
            eventFeedBody.host.removeAttribute("visibility");
            const naturalHeight = Math.max(270, 206 + bodyHeight);
            const scaleX = appearance.width / 1600;
            const scaleY = appearance.height / naturalHeight;
            const geometryGroup = svgElement("g", {
              class: "overlay",
              transform: `translate(${appearance.x} ${appearance.y + appearance.height}) scale(${scaleX} ${scaleY}) translate(0 ${-naturalHeight})`,
              "data-source-card-id": String(card.id),
            });
            const presentationGroup = svgElement("g", {
              class: "event-feed-presentation",
            });
            presentationGroup.append(
              svgElement("rect", { class: "event-feed-card card", x: "0", y: "0", width: "1600", height: String(naturalHeight), rx: "30" }),
              svgElement("rect", { class: "event-feed-accent accent", x: "0", y: "0", width: "16", height: String(naturalHeight), rx: "8" }),
            );
            appendText(presentationGroup, "event-feed-kicker", 56, 58, card.kind.replace(/([A-Z])/g, " $1").toUpperCase());
            appendText(presentationGroup, "event-feed-title", 56, 128, card.title);
            presentationGroup.append(eventFeedBody.host);
            geometryGroup.append(presentationGroup);
            canvas.append(geometryGroup);
          };

          const clearPresentationAnimation = () => {
            if (presentationAnimationTimer !== null) {
              window.clearTimeout(presentationAnimationTimer);
              presentationAnimationTimer = null;
            }
            delete root.dataset.animation;
          };

          const applyAnimation = (animation, durationMilliseconds) => {
            clearPresentationAnimation();
            if (
              animation !== "entrance" &&
              animation !== "statusChange" &&
              animation !== "result" &&
              animation !== "winner"
              && animation !== "card"
              && animation !== "sample"
            ) {
              return;
            }

            root.dataset.animation = animation;
            const animationDuration =
              animation === "result" || animation === "winner"
                ? durationMilliseconds
                : 700;
            presentationAnimationTimer = window.setTimeout(() => {
              delete root.dataset.animation;
              presentationAnimationTimer = null;
            }, animationDuration);
          };

          const applyPresentationAnimation = (
            animation,
            durationMilliseconds,
            fromDraft,
          ) => {
            if (!fromDraft) {
              applyAnimation(animation, durationMilliseconds);
            }
          };

          let dashboardDraft = null;
          let savedPresentation = null;
          let draftSheet = null;
          let draftBaseRuleCount = 0;

          const clearDraftCss = () => {
            const sheet = appearanceStylesheet.sheet;
            if (!(sheet instanceof CSSStyleSheet)) return null;
            if (sheet !== draftSheet) {
              draftSheet = sheet;
              draftBaseRuleCount = sheet.cssRules.length;
            }
            while (sheet.cssRules.length > draftBaseRuleCount) {
              sheet.deleteRule(sheet.cssRules.length - 1);
            }
            return sheet;
          };

          const applyDraftCss = (css) => {
            const sheet = clearDraftCss();
            if (sheet === null || typeof css !== "string" || css.length === 0) return;
            for (const rule of css.matchAll(/([^{}]+)\{([^{}]+)\}/g)) {
              try {
                sheet.insertRule(`${rule[1]} { ${rule[2]} }`, sheet.cssRules.length);
              } catch (error) {
                if (!(error instanceof DOMException)) throw error;
              }
            }
          };

          const acknowledgeDashboardDraft = () => {
            if (
              dashboardDraft === null ||
              typeof dashboardDraft.requestId !== "string" ||
              typeof dashboardDraft.overlayId !== "string"
            ) return;
            window.parent.postMessage(
              {
                kind: "blokebot-dashboard-draft-ready",
                requestId: dashboardDraft.requestId,
                overlayId: dashboardDraft.overlayId,
              },
              window.location.origin,
            );
          };

          const withDashboardDraft = (projection) => {
            if (dashboardDraft === null || !validAppearance(dashboardDraft.appearance)) return projection;
            const state = { ...projection.state };
            if (projection.overlayType === "guessing" && dashboardDraft.choices?.showGuessCount === false && state.phase !== "completed") state.guessCount = null;
            if (projection.overlayType === "giveaway") {
              state.title = dashboardDraft.choices?.giveawayTitle ?? state.title;
              if (dashboardDraft.choices?.showEntrantCount === false) state.entrantCount = null;
              if (dashboardDraft.choices?.showCountdown === false) state.closesAtUtc = null;
              if (dashboardDraft.choices?.showJoinCommand === false) state.joinCommand = null;
            }
            return { ...projection, state, appearance: dashboardDraft.appearance };
          };

          const applyPresentation = (projection, sequence, epoch, occurredAtUtc, fromDraft = false) => {
            if (!fromDraft) savedPresentation = { projection, sequence, epoch, occurredAtUtc };
            projection = withDashboardDraft(projection);
            if (projection?.schemaVersion !== 1) {
              return false;
            }
            if (fromDraft) {
              clearPresentationAnimation();
            }
            if (projection.overlayType === "empty") {
              if (
                typeof projection.state !== "object" ||
                projection.state === null
              ) {
                return false;
              }
              canvas.replaceChildren();
              delete root.dataset.phase;
              applyPresentationAnimation("none", 0, fromDraft);
            } else if (projection.overlayType === "guessing") {
              if (
                !Number.isSafeInteger(projection.resultDurationMilliseconds) ||
                projection.resultDurationMilliseconds < 1000 ||
                projection.resultDurationMilliseconds > 30000 ||
                !validGuessingState(projection.state) ||
                !validAppearance(projection.appearance)
              ) {
                return false;
              }
              renderGuessing(projection.state, projection.appearance);
              applyPresentationAnimation(
                typeof projection.animation === "string"
                  ? projection.animation
                  : "none",
                projection.resultDurationMilliseconds,
                fromDraft,
              );
            } else if (projection.overlayType === "cuePlayer") {
              if (
                typeof projection.state !== "object" ||
                projection.state === null
              ) {
                return false;
              }
              canvas.replaceChildren();
              clearCues();
              delete root.dataset.phase;
              applyPresentationAnimation("none", 0, fromDraft);
            } else if (projection.overlayType === "giveaway") {
              if (
                !Number.isSafeInteger(
                  projection.winnerAnimationDurationMilliseconds,
                ) ||
                projection.winnerAnimationDurationMilliseconds < 1000 ||
                projection.winnerAnimationDurationMilliseconds > 10000 ||
                !validGiveawayState(projection.state) ||
                !validAppearance(projection.appearance)
              ) {
                return false;
              }
              renderGiveaway(projection.state, projection.appearance);
              applyPresentationAnimation(
                typeof projection.animation === "string"
                  ? projection.animation
                  : "none",
                projection.winnerAnimationDurationMilliseconds,
                fromDraft,
              );
            } else if (projection.overlayType === "eventFeed") {
              if (!validEventFeedState(projection.state) || !validAppearance(projection.appearance)) return false;
              renderEventFeed(projection.state, projection.appearance);
              applyPresentationAnimation(
                typeof projection.animation === "string"
                  ? projection.animation
                  : "none",
                700,
                fromDraft,
              );
            } else {
              return false;
            }

            root.dataset.overlayType = projection.overlayType;
            root.dataset.schemaVersion = String(projection.schemaVersion);
            root.dataset.serverEpoch = epoch;
            root.dataset.sequence = String(sequence);
            root.dataset.generatedAtUtc = occurredAtUtc;
            window.requestAnimationFrame(() => {
              applyDraftCss(dashboardDraft?.css ?? "");
              acknowledgeDashboardDraft();
            });
            return true;
          };

          if (credentials === "same-origin" && window.parent !== window) {
            window.addEventListener("message", (event) => {
              if (event.origin !== window.location.origin || event.source !== window.parent) return;
              const value = event.data;
              if (typeof value !== "object" || value === null || value.kind !== "blokebot-dashboard-draft" || typeof value.overlayId !== "string") return;
              const expectedPath = `${window.location.pathname.replace(/\/$/, "")}`;
              if (!expectedPath.endsWith(`/overlays/preview/${value.overlayId}`)) return;
              if (!validAppearance(value.appearance) || typeof value.css !== "string" || value.css.length > 16384) return;
              if (typeof value.requestId !== "string") return;
              dashboardDraft = { requestId: value.requestId, overlayId: value.overlayId, appearance: value.appearance, css: value.css, choices: value.choices };
              if (savedPresentation !== null) applyPresentation(savedPresentation.projection, savedPresentation.sequence, savedPresentation.epoch, savedPresentation.occurredAtUtc, true);
            });
          }

          const clearCues = () => {
            cueCanvas.replaceChildren();
            for (const timers of cueTimers.values()) {
              for (const timer of timers) {
                window.clearTimeout(timer);
              }
            }
            cueTimers.clear();
          };

          const validRectangle = (rectangle) =>
            typeof rectangle === "object" &&
            rectangle !== null &&
            ["xPercent", "yPercent", "widthPercent", "heightPercent"].every(
              (name) =>
                typeof rectangle[name] === "number" &&
                Number.isFinite(rectangle[name]),
            );

          const layerElement = (layer) => {
            let element;
            if (layer.kind === "externalWeb" && typeof layer.url === "string") {
              element = document.createElement("iframe");
              element.src = layer.url;
              element.setAttribute("sandbox", "allow-scripts");
              element.referrerPolicy = "no-referrer";
              element.title = "External cue content";
            } else if (
              (layer.kind === "uploadedMedia" ||
                layer.kind === "remoteMedia") &&
              (layer.mediaKind === "video" ||
                layer.mediaKind === "audio" ||
                (layer.kind === "uploadedMedia" && layer.mediaKind === "image"))
            ) {
              element = document.createElement(
                layer.mediaKind === "image" ? "img" : layer.mediaKind,
              );
              if (layer.mediaKind === "image") {
                element.alt = "";
                element.decoding = "async";
              } else {
                element.autoplay = true;
                element.preload = "auto";
                element.controls = false;
                element.volume =
                  typeof layer.volume === "number"
                    ? Math.min(1, Math.max(0, layer.volume))
                    : 1;
              }
              if (layer.kind === "remoteMedia" && typeof layer.url === "string") {
                element.src = layer.url;
              } else if (
                typeof layer.assetId === "string" &&
                Number.isSafeInteger(layer.contentRevision)
              ) {
                element.src = `${root.dataset.mediaUrl}/${encodeURIComponent(
                  layer.assetId,
                )}/${layer.contentRevision}`;
              } else {
                return null;
              }
              element.style.objectFit =
                layer.fit === "cover" || layer.fit === "fill"
                  ? layer.fit
                  : "contain";
            } else {
              return null;
            }

            if (!validRectangle(layer.rectangle)) {
              return null;
            }
            element.className = "cue-layer";
            element.style.left = `${layer.rectangle.xPercent}%`;
            element.style.top = `${layer.rectangle.yPercent}%`;
            element.style.width = `${layer.rectangle.widthPercent}%`;
            element.style.height = `${layer.rectangle.heightPercent}%`;
            element.style.zIndex = String(layer.zIndex);
            return element;
          };

          const completeCue = async (runId) => {
            const run = cueCanvas.querySelector(`[data-cue-run="${CSS.escape(runId)}"]`);
            run?.remove();
            const timers = cueTimers.get(runId) ?? [];
            for (const timer of timers) {
              window.clearTimeout(timer);
            }
            cueTimers.delete(runId);
            try {
              await fetch(
                `${root.dataset.completionUrl}/${encodeURIComponent(runId)}`,
                {
                  method: "POST",
                  credentials,
                  cache: "no-store",
                },
              );
            } catch {
              // Server-side expiry still advances the transient queue.
            }
          };

          const renderCue = (payload) => {
            if (
              payload?.overlayType !== "cuePlayer" ||
              payload.schemaVersion !== 1 ||
              typeof payload.runId !== "string" ||
              !Number.isSafeInteger(payload.durationMilliseconds) ||
              payload.durationMilliseconds < 100 ||
              payload.durationMilliseconds > 300000 ||
              !Array.isArray(payload.layers)
            ) {
              return false;
            }
            const run = document.createElement("div");
            run.className = "cue-run";
            run.dataset.cueRun = payload.runId;
            cueCanvas.append(run);
            const timers = [];
            for (const layer of payload.layers) {
              if (
                !Number.isSafeInteger(layer.startOffsetMilliseconds) ||
                !Number.isSafeInteger(layer.durationMilliseconds) ||
                !Number.isSafeInteger(layer.zIndex)
              ) {
                continue;
              }
              timers.push(
                window.setTimeout(() => {
                  const element = layerElement(layer);
                  if (element === null) {
                    return;
                  }
                  run.append(element);
                  timers.push(
                    window.setTimeout(
                      () => element.remove(),
                      layer.durationMilliseconds,
                    ),
                  );
                }, layer.startOffsetMilliseconds),
              );
            }
            timers.push(
              window.setTimeout(
                () => void completeCue(payload.runId),
                payload.durationMilliseconds,
              ),
            );
            cueTimers.set(payload.runId, timers);
            return true;
          };

          const stopCue = (runId) => {
            if (typeof runId !== "string") {
              return false;
            }
            const run = cueCanvas.querySelector(`[data-cue-run="${CSS.escape(runId)}"]`);
            run?.remove();
            const timers = cueTimers.get(runId) ?? [];
            for (const timer of timers) {
              window.clearTimeout(timer);
            }
            cueTimers.delete(runId);
            return true;
          };

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

          window.addEventListener(
            "pagehide",
            () => {
              pageLifetime.abort();
              if (testPulseTimer !== null) {
                window.clearTimeout(testPulseTimer);
              }
              if (presentationAnimationTimer !== null) {
                window.clearTimeout(presentationAnimationTimer);
              }
              if (giveawayCountdownTimer !== null) {
                window.clearTimeout(giveawayCountdownTimer);
              }
              clearCues();
            },
            { once: true },
          );
          void run();
        })();
        """;
}
