namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _viewerQueue = """
          const validViewerQueueState = (state) => {
            const validField = (field) =>
              typeof field === "object" &&
              field !== null &&
              typeof field.key === "string" &&
              typeof field.label === "string" &&
              typeof field.value === "string";
            const validEntry = (entry) =>
              typeof entry === "object" &&
              entry !== null &&
              (entry.displayName === null || typeof entry.displayName === "string") &&
              Array.isArray(entry.fields) &&
              entry.fields.length <= 12 &&
              entry.fields.every(validField);
            return (
              typeof state === "object" &&
              state !== null &&
              typeof state.queueName === "string" &&
              typeof state.activityName === "string" &&
              typeof state.isOpen === "boolean" &&
              Number.isSafeInteger(state.totalQueueSize) &&
              state.totalQueueSize >= 0 &&
              Array.isArray(state.currentParty) &&
              state.currentParty.length <= 12 &&
              state.currentParty.every(validEntry) &&
              Array.isArray(state.next) &&
              state.next.length <= 12 &&
              state.next.every(validEntry)
            );
          };

          const viewerQueueEntryText = (entry, position) => {
            const name = entry.displayName ?? `Player ${position}`;
            const fields = entry.fields
              .filter((field) => field.value.length > 0)
              .map((field) => `${field.label}: ${field.value}`)
              .join(" · ");
            return fields.length === 0 ? name : `${name} · ${fields}`;
          };

          const renderViewerQueue = (state, appearance) => {
            canvas.replaceChildren();
            const definitions = svgElement("defs", {});
            appendTextClip(
              definitions,
              "viewer-queue-title-clip",
              48,
              70,
              1104,
              66,
            );
            appendTextClip(
              definitions,
              "viewer-queue-detail-clip",
              48,
              138,
              1104,
              38,
            );
            const geometryGroup = svgElement("g", {
              class: "overlay",
              transform: `translate(${appearance.x} ${appearance.y}) scale(${appearance.width / 1200} ${appearance.height / 800})`,
            });
            const presentationGroup = svgElement("g", {
              class: "viewer-queue-presentation",
            });
            presentationGroup.append(
              definitions,
              svgElement("rect", {
                class: "viewer-queue-card card",
                x: "0",
                y: "0",
                width: "1200",
                height: "800",
                rx: "30",
              }),
              svgElement("rect", {
                class: "viewer-queue-accent accent",
                x: "0",
                y: "0",
                width: "16",
                height: "800",
                rx: "8",
              }),
            );
            geometryGroup.append(presentationGroup);
            canvas.append(geometryGroup);
            appendText(
              presentationGroup,
              "viewer-queue-kicker kicker",
              48,
              56,
              state.isOpen ? "QUEUE OPEN" : "QUEUE CLOSED",
            );
            appendFittedText(
              presentationGroup,
              "viewer-queue-title title",
              48,
              124,
              state.queueName,
              1104,
              "viewer-queue-title-clip",
            );
            appendFittedText(
              presentationGroup,
              "viewer-queue-detail detail",
              48,
              168,
              `${state.activityName} · ${state.totalQueueSize} waiting`,
              1104,
              "viewer-queue-detail-clip",
            );
            appendText(presentationGroup, "viewer-queue-section", 48, 224, "CURRENT PARTY");
            appendText(presentationGroup, "viewer-queue-section", 624, 224, "NEXT");
            state.currentParty.forEach((entry, index) => {
              const clipPathId = `viewer-queue-current-entry-${index}-clip`;
              appendTextClip(definitions, clipPathId, 48, 240 + index * 40, 528, 36);
              appendFittedText(
                presentationGroup,
                "viewer-queue-entry",
                48,
                268 + index * 40,
                viewerQueueEntryText(entry, index + 1),
                528,
                clipPathId,
              );
            });
            state.next.forEach((entry, index) => {
              const clipPathId = `viewer-queue-next-entry-${index}-clip`;
              appendTextClip(definitions, clipPathId, 624, 240 + index * 40, 528, 36);
              appendFittedText(
                presentationGroup,
                "viewer-queue-entry",
                624,
                268 + index * 40,
                viewerQueueEntryText(entry, index + 1),
                528,
                clipPathId,
              );
            });
          };
        """;
}
