namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _eventFeed = """
          const validEventFeedState = (state) => {
            if (typeof state !== "object" || state === null || !Array.isArray(state.pending)) return false;
            const validCard = (card) =>
              typeof card === "object" &&
              card !== null &&
              Number.isSafeInteger(card.id) &&
              card.id >= 0 &&
              (card.kind === "pointAward" ||
                card.kind === "guessingWinner" ||
                card.kind === "giveawayWinner" ||
                card.kind === "bingoEvent" ||
                card.kind === "achievementCompletion") &&
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
        """;
}
