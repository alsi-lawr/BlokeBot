namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _progress = """
          const validProgressState = (state) =>
            typeof state === "object" &&
            state !== null &&
            Array.isArray(state.items) &&
            state.items.length <= 12 &&
            state.items.every((item) =>
              typeof item?.id === "string" &&
              typeof item.context === "string" &&
              typeof item.title === "string" &&
              typeof item.current === "string" &&
              typeof item.target === "string" &&
              Number.isSafeInteger(item.percentage) &&
              item.percentage >= 0 &&
              item.percentage <= 100 &&
              Number.isSafeInteger(item.completionCount) &&
              item.completionCount >= 0 &&
              !Number.isNaN(Date.parse(item.expiresAtUtc)) &&
              ["active", "accepted", "completed", "failed", "expired"].includes(
                String(item.state).toLowerCase(),
              ) &&
              Array.isArray(item.recentContributors) &&
              item.recentContributors.length <= 5 &&
              item.recentContributors.every(
                (contributor) =>
                  typeof contributor?.login === "string" &&
                  typeof contributor.amount === "string",
              ),
            );

          const progressStatus = (state) => {
            if (state === "completed") return "COMPLETED";
            if (state === "failed") return "FAILED";
            if (state === "expired") return "EXPIRED";
            if (state === "accepted") return "ACCEPTED";
            return "IN PROGRESS";
          };

          const progressExpiry = (item, generatedAtUtc) => {
            const state = String(item.state).toLowerCase();
            if (state === "completed" || state === "failed" || state === "expired") {
              return progressStatus(state);
            }
            const remaining = Date.parse(item.expiresAtUtc) - Date.parse(generatedAtUtc);
            if (remaining <= 0) return "Deadline reached";
            const days = Math.ceil(remaining / 86400000);
            return days === 1 ? "Ends in 1 day" : `Ends in ${days} days`;
          };

          const renderProgress = (state, appearance, rotationSeconds, overlayType, generatedAtUtc) => {
            if (progressRotationTimer !== null) {
              window.clearTimeout(progressRotationTimer);
              progressRotationTimer = null;
            }
            canvas.replaceChildren();
            if (state.items.length === 0) {
              delete root.dataset.progressState;
              return;
            }

            const renderItem = (index) => {
              canvas.replaceChildren();
              const item = state.items[index];
              const itemState = String(item.state).toLowerCase();
              root.dataset.progressState = itemState;
              const definitions = svgElement("defs", {});
              appendTextClip(definitions, "progress-overlay-title-clip", 30, 60, 620, 54);
              const geometryGroup = svgElement("g", {
                class: "overlay",
                transform: `translate(${appearance.x} ${appearance.y}) scale(${appearance.width / 680} ${appearance.height / 340})`,
              });
              const presentationGroup = svgElement("g", { class: "progress-overlay-presentation" });
              presentationGroup.append(
                definitions,
                svgElement("rect", { class: "progress-overlay-card card", x: "0", y: "0", width: "680", height: "340", rx: "22" }),
                svgElement("rect", { class: "progress-overlay-accent accent", x: "0", y: "0", width: "9", height: "340", rx: "5" }),
              );
              appendText(
                presentationGroup,
                "progress-overlay-kicker kicker",
                30,
                38,
                `${overlayType === "communityGoal" ? "COMMUNITY GOAL" : "VIEWER-FUNDED BOUNTY"}${state.items.length > 1 ? ` · ${index + 1} OF ${state.items.length}` : ""}`,
              );
              appendText(presentationGroup, "progress-overlay-context detail", 650, 38, item.context);
              appendFittedText(presentationGroup, "progress-overlay-title title", 30, 99, item.title, 620, "progress-overlay-title-clip");
              appendText(presentationGroup, "progress-overlay-result result", 30, 142, `${item.current} / ${item.target}`);
              appendText(presentationGroup, "progress-overlay-context detail", 650, 142, `${item.percentage}%`);
              presentationGroup.append(
                svgElement("rect", { class: "progress-overlay-track", x: "30", y: "160", width: "620", height: "14", rx: "7" }),
                svgElement("rect", { class: "progress-overlay-fill accent", x: "30", y: "160", width: String(620 * item.percentage / 100), height: "14", rx: "7" }),
              );
              appendText(presentationGroup, "progress-overlay-detail detail", 30, 210, progressStatus(itemState));
              appendText(presentationGroup, "progress-overlay-context detail", 650, 210, progressExpiry(item, generatedAtUtc));
              if (item.recentContributors.length > 0) {
                appendText(
                  presentationGroup,
                  "progress-overlay-contributors detail",
                  30,
                  272,
                  item.recentContributors.map((value) => `@${value.login} +${value.amount}`).join("   •   "),
                );
              }
              geometryGroup.append(presentationGroup);
              canvas.append(geometryGroup);
              if (state.items.length > 1) {
                progressRotationTimer = window.setTimeout(() => {
                  applyAnimation("statusChange", 700);
                  renderItem((index + 1) % state.items.length);
                }, rotationSeconds * 1000);
              }
            };
            renderItem(0);
          };
        """;
}
