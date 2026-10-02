namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _text = """
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

          const stableTextClass = (className) =>
            className.endsWith("-kicker")
              ? " kicker"
              : className.endsWith("-title")
                ? " title"
                : className.endsWith("-result")
                  ? " result"
                  : " detail";

          const appendText = (group, className, x, y, text) => {
            group.append(
              svgElement(
                "text",
                {
                  class: className + stableTextClass(className),
                  x: String(x),
                  y: String(y),
                },
                text,
              ),
            );
          };

          const fitSvgText = (element) => {
            const fullText = element.getAttribute("aria-label");
            const maximumWidth = Number(element.dataset.fitWidth);
            if (
              fullText === null ||
              !Number.isFinite(maximumWidth) ||
              maximumWidth <= 0
            ) {
              return;
            }

            element.removeAttribute("textLength");
            element.removeAttribute("lengthAdjust");
            element.textContent = fullText;
            if (element.getComputedTextLength() > maximumWidth) {
              const characters = Array.from(fullText);
              let fittingLength = 0;
              let rejectedLength = characters.length;
              while (fittingLength < rejectedLength) {
                const candidateLength = Math.ceil(
                  (fittingLength + rejectedLength) / 2,
                );
                element.textContent =
                  characters.slice(0, candidateLength).join("") + "…";
                if (element.getComputedTextLength() <= maximumWidth) {
                  fittingLength = candidateLength;
                } else {
                  rejectedLength = candidateLength - 1;
                }
              }
              element.textContent =
                characters.slice(0, fittingLength).join("") + "…";
              if (element.getComputedTextLength() > maximumWidth) {
                element.setAttribute("textLength", String(maximumWidth));
                element.setAttribute("lengthAdjust", "spacingAndGlyphs");
              }
            }
            element.prepend(svgElement("title", {}, fullText));
          };

          const appendFittedText = (
            group,
            className,
            x,
            y,
            text,
            maximumWidth,
            clipPathId,
          ) => {
            const element = svgElement(
              "text",
              {
                class: className + stableTextClass(className),
                x: String(x),
                y: String(y),
                "aria-label": text,
                "data-fit-width": String(maximumWidth),
                "clip-path": `url(#${clipPrefix}${clipPathId})`,
              },
              text,
            );
            group.append(element);
            fitSvgText(element);
          };

          const appendTextClip = (
            definitions,
            id,
            x,
            y,
            width,
            height,
          ) => {
            const clipPath = svgElement("clipPath", { id: clipPrefix + id });
            clipPath.append(
              svgElement("rect", {
                x: String(x),
                y: String(y),
                width: String(width),
                height: String(height),
              }),
            );
            definitions.append(clipPath);
          };

          const refitFittedText = () => {
            for (const element of canvas.querySelectorAll("text[data-fit-width]")) {
              if (element instanceof SVGTextElement) {
                fitSvgText(element);
              }
            }
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
        """;
}
