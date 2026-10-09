namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _guessing = """
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
        """;
}
