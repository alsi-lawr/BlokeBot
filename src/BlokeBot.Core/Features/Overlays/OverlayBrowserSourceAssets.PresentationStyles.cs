namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    internal const string PresentationStylesheet = """
        .guessing-card,
        .giveaway-card,
        .event-feed-card,
        .viewer-queue-card,
        .progress-overlay-card {
          fill: rgba(15, 23, 42, 0.94);
          stroke: rgba(148, 163, 184, 0.72);
          stroke-width: 2;
        }

        .guessing-accent,
        .giveaway-accent,
        .event-feed-accent,
        .viewer-queue-accent,
        .progress-overlay-accent,
        .progress-overlay-fill {
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
        :is(#overlay-root, [data-overlay-root])[data-animation="card"] .event-feed-presentation,
        :is(#overlay-root, [data-overlay-root])[data-animation="sample"] .event-feed-presentation { animation: event-feed-card 520ms cubic-bezier(0.22, 1, 0.36, 1); }
        @keyframes event-feed-card { from { opacity: 0; transform: translateX(-60px); } to { opacity: 1; transform: translateX(0); } }

        .viewer-queue-kicker,
        .viewer-queue-title,
        .viewer-queue-detail,
        .viewer-queue-section,
        .viewer-queue-entry {
          fill: #f8fafc;
          font-family: ui-sans-serif, system-ui, sans-serif;
        }
        .viewer-queue-kicker { fill: #93c5fd; font-size: 26px; font-weight: 800; letter-spacing: 4px; }
        .viewer-queue-title { font-size: 52px; font-weight: 800; }
        .viewer-queue-detail { fill: #cbd5e1; font-size: 24px; font-weight: 600; }
        .viewer-queue-section { fill: #93c5fd; font-size: 24px; font-weight: 800; letter-spacing: 2px; }
        .viewer-queue-entry { font-size: 22px; font-weight: 650; }
        :is(#overlay-root, [data-overlay-root])[data-animation="partyChange"] .viewer-queue-presentation,
        :is(#overlay-root, [data-overlay-root])[data-animation="readyOutcome"] .viewer-queue-presentation,
        :is(#overlay-root, [data-overlay-root])[data-animation="selectedNext"] .viewer-queue-presentation {
          animation: viewer-queue-change 520ms cubic-bezier(0.22, 1, 0.36, 1);
        }
        @keyframes viewer-queue-change {
          from { opacity: 0.3; transform: translateY(24px); }
          to { opacity: 1; transform: translateY(0); }
        }

        .progress-overlay-kicker,
        .progress-overlay-context,
        .progress-overlay-title,
        .progress-overlay-detail,
        .progress-overlay-result,
        .progress-overlay-contributors {
          fill: #f8fafc;
          font-family: ui-sans-serif, system-ui, sans-serif;
        }
        .progress-overlay-kicker { fill: #5eead4; font-size: 20px; font-weight: 850; letter-spacing: 3px; }
        .progress-overlay-context { fill: #a5b4fc; font-size: 18px; font-weight: 750; text-anchor: end; }
        .progress-overlay-title { font-size: 34px; font-weight: 850; }
        .progress-overlay-detail { fill: #cbd5e1; font-size: 20px; font-weight: 650; }
        .progress-overlay-result { fill: #f8fafc; font-size: 23px; font-weight: 800; }
        .progress-overlay-contributors { fill: #cbd5e1; font-size: 16px; font-weight: 650; }
        .progress-overlay-track { fill: rgba(255, 255, 255, 0.14); }
        :is(#overlay-root, [data-overlay-root])[data-progress-state="completed"] .progress-overlay-accent,
        :is(#overlay-root, [data-overlay-root])[data-progress-state="completed"] .progress-overlay-fill { fill: #34d399; }
        :is(#overlay-root, [data-overlay-root])[data-progress-state="failed"] .progress-overlay-accent,
        :is(#overlay-root, [data-overlay-root])[data-progress-state="failed"] .progress-overlay-fill { fill: #f87171; }
        :is(#overlay-root, [data-overlay-root])[data-progress-state="expired"] .progress-overlay-accent,
        :is(#overlay-root, [data-overlay-root])[data-progress-state="expired"] .progress-overlay-fill { fill: #fbbf24; }
        :is(#overlay-root, [data-overlay-root])[data-progress-state="accepted"] .progress-overlay-accent,
        :is(#overlay-root, [data-overlay-root])[data-progress-state="accepted"] .progress-overlay-fill { fill: #a78bfa; }
        :is(#overlay-root, [data-overlay-root])[data-animation="progress"] .progress-overlay-fill { animation: progress-overlay-fill 620ms ease-out; transform-origin: left; }
        :is(#overlay-root, [data-overlay-root])[data-animation="complete"] .progress-overlay-presentation { animation: progress-overlay-complete 760ms cubic-bezier(0.34, 1.56, 0.64, 1); }
        :is(#overlay-root, [data-overlay-root])[data-animation="statusChange"] .progress-overlay-presentation { animation: guessing-overlay-status 360ms ease-out; }
        @keyframes progress-overlay-fill { from { transform: scaleX(.72); } to { transform: scaleX(1); } }
        @keyframes progress-overlay-complete { from { opacity: .35; transform: scale(.94); } to { opacity: 1; transform: scale(1); } }

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

        :is(#overlay-root, [data-overlay-root])[data-animation="winner"] .giveaway-presentation {
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

        :is(#overlay-root, [data-overlay-root])[data-animation="entrance"] .guessing-presentation {
          animation: guessing-overlay-entrance 480ms cubic-bezier(0.22, 1, 0.36, 1);
        }

        :is(#overlay-root, [data-overlay-root])[data-animation="statusChange"] .guessing-presentation {
          animation: guessing-overlay-status 360ms ease-out;
        }

        :is(#overlay-root, [data-overlay-root])[data-animation="result"] .guessing-presentation {
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
          :is(#overlay-root, [data-overlay-root])[data-animation] .guessing-presentation,
          :is(#overlay-root, [data-overlay-root])[data-animation] .giveaway-presentation,
          :is(#overlay-root, [data-overlay-root])[data-animation] .event-feed-presentation,
          :is(#overlay-root, [data-overlay-root])[data-animation] .viewer-queue-presentation,
          :is(#overlay-root, [data-overlay-root])[data-test-pulse="active"] #overlay-canvas {
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
}
