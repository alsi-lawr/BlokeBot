namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _giveaway = """
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
        """;
}
