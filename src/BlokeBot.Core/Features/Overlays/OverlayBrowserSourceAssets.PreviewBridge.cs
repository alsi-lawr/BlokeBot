namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _previewBridge = """
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
              && animation !== "partyChange"
              && animation !== "readyOutcome"
              && animation !== "selectedNext"
              && animation !== "progress"
              && animation !== "complete"
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
                const selectors = options.appearanceSelector
                  ? rule[1].split(",").map(selector => `${options.appearanceSelector} ${selector.trim()}`).join(",")
                  : rule[1];
                sheet.insertRule(`${selectors} { ${rule[2]} }`, sheet.cssRules.length);
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
            if (projection.overlayType === "viewerQueue") {
              if (Number.isSafeInteger(dashboardDraft.choices?.currentRows)) {
                state.currentParty = state.currentParty.slice(0, dashboardDraft.choices.currentRows);
              }
              if (Number.isSafeInteger(dashboardDraft.choices?.nextRows)) {
                state.next = state.next.slice(0, dashboardDraft.choices.nextRows);
              }
            }
            const rotationSeconds =
              (projection.overlayType === "communityGoal" || projection.overlayType === "viewerFundedBounty") &&
              Number.isSafeInteger(dashboardDraft.choices?.rotationSeconds)
                ? dashboardDraft.choices.rotationSeconds
                : projection.rotationSeconds;
            return { ...projection, state, rotationSeconds, appearance: dashboardDraft.appearance };
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
            } else if (projection.overlayType === "viewerQueue") {
              if (!validViewerQueueState(projection.state) || !validAppearance(projection.appearance)) return false;
              renderViewerQueue(projection.state, projection.appearance);
              applyPresentationAnimation(
                typeof projection.animation === "string"
                  ? projection.animation
                  : "none",
                700,
                fromDraft,
              );
            } else if (projection.overlayType === "communityGoal" || projection.overlayType === "viewerFundedBounty") {
              if (
                !Number.isSafeInteger(projection.rotationSeconds) ||
                projection.rotationSeconds < 5 ||
                projection.rotationSeconds > 120 ||
                !validProgressState(projection.state) ||
                !validAppearance(projection.appearance)
              ) return false;
              renderProgress(
                projection.state,
                projection.appearance,
                projection.rotationSeconds,
                projection.overlayType,
                occurredAtUtc,
              );
              applyPresentationAnimation(
                typeof projection.animation === "string" ? projection.animation : "none",
                900,
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
              applyDraftCss(dashboardDraft?.css ??
                (options.appearanceSelector ? projection.appearance?.css ?? "" : ""));
              refitFittedText();
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
            }, { signal: pageLifetime.signal });
          }
        """;
}
