const registrations = new Map();
const boundaries = new Map();
const boundaryInteractions = new WeakMap();

let intersectionObserver;
let resizeObserver;
let interactionSequence = 0;
let updateFrame;

function isActive(registration) {
    return registration.region.dataset.saveActive === "true";
}

function isRendered(registration) {
    return (
        !registration.region.closest("[hidden], [inert], details:not([open])") &&
        registration.region.getClientRects().length > 0
    );
}

function isEligible(registration) {
    return (
        isActive(registration) &&
        registration.boundaryState.intersects &&
        isRendered(registration)
    );
}

function interactionOrder(registration) {
    return boundaryInteractions.get(registration.boundary) ?? 0;
}

function viewportProximity(registration) {
    const rect = registration.boundary.getBoundingClientRect();
    const viewportHeight = window.innerHeight;
    const visibleTop = Math.max(0, Math.min(viewportHeight, rect.top));
    const visibleBottom = Math.max(0, Math.min(viewportHeight, rect.bottom));
    return Math.abs((visibleTop + visibleBottom) / 2 - viewportHeight / 2);
}

function documentOrder(left, right) {
    const position = left.region.compareDocumentPosition(right.region);
    if (position & Node.DOCUMENT_POSITION_FOLLOWING) {
        return -1;
    }
    if (position & Node.DOCUMENT_POSITION_PRECEDING) {
        return 1;
    }
    return 0;
}

function compareOwners(left, right) {
    const interactionDifference = interactionOrder(right) - interactionOrder(left);
    if (interactionDifference !== 0) {
        return interactionDifference;
    }

    const proximityDifference = viewportProximity(left) - viewportProximity(right);
    return proximityDifference !== 0 ? proximityDifference : documentOrder(left, right);
}

function updateBoundaryEnrollment() {
    for (const [boundary, state] of boundaries) {
        const active = [...state.registrations].some(
            (registration) => isActive(registration) && isRendered(registration),
        );
        boundary.dataset.stickySaveBoundaryActive = String(active);
    }
}

function updateOwnership() {
    updateFrame = undefined;
    updateBoundaryEnrollment();

    const candidates = [...registrations.values()].filter(isEligible);
    const modalCandidates = candidates.filter(
        (registration) => registration.region.dataset.saveScope === "modal",
    );
    const owner = (modalCandidates.length > 0 ? modalCandidates : candidates).sort(
        compareOwners,
    )[0];

    for (const registration of registrations.values()) {
        registration.region.dataset.saveVisible = String(registration === owner);
    }
}

function scheduleOwnershipUpdate() {
    if (updateFrame === undefined) {
        updateFrame = window.requestAnimationFrame(updateOwnership);
    }
}

function recordInteraction(event) {
    const target = event.target instanceof Element ? event.target : event.target?.parentElement;
    const boundary = target?.closest("[data-sticky-save-scope]");
    if (!boundary) {
        return;
    }

    boundaryInteractions.set(boundary, ++interactionSequence);
    scheduleOwnershipUpdate();
}

function startCoordinator() {
    if (intersectionObserver) {
        return;
    }

    intersectionObserver = new IntersectionObserver(
        (entries) => {
            for (const entry of entries) {
                const state = boundaries.get(entry.target);
                if (state) {
                    state.intersects = entry.isIntersecting;
                }
            }
            scheduleOwnershipUpdate();
        },
        { rootMargin: "-1px 0px -1px 0px" },
    );
    resizeObserver = new ResizeObserver(scheduleOwnershipUpdate);
    document.addEventListener("focusin", recordInteraction, true);
    document.addEventListener("pointerdown", recordInteraction, true);
    document.addEventListener("toggle", scheduleOwnershipUpdate, true);
    window.addEventListener("scroll", scheduleOwnershipUpdate, { capture: true, passive: true });
    window.addEventListener("resize", scheduleOwnershipUpdate);
}

function stopCoordinator() {
    if (!intersectionObserver || registrations.size > 0) {
        return;
    }

    intersectionObserver.disconnect();
    resizeObserver.disconnect();
    intersectionObserver = undefined;
    resizeObserver = undefined;
    document.removeEventListener("focusin", recordInteraction, true);
    document.removeEventListener("pointerdown", recordInteraction, true);
    document.removeEventListener("toggle", scheduleOwnershipUpdate, true);
    window.removeEventListener("scroll", scheduleOwnershipUpdate, true);
    window.removeEventListener("resize", scheduleOwnershipUpdate);
    if (updateFrame !== undefined) {
        window.cancelAnimationFrame(updateFrame);
        updateFrame = undefined;
    }
}

function initialIntersection(boundary) {
    const rect = boundary.getBoundingClientRect();
    return rect.bottom > 1 && rect.top < window.innerHeight - 1;
}

export function register(region) {
    const boundary = region.closest("[data-sticky-save-scope]") ?? region.parentElement;
    if (!boundary) {
        return { dispose() {} };
    }

    startCoordinator();
    let boundaryState = boundaries.get(boundary);
    if (!boundaryState) {
        boundaryState = {
            intersects: initialIntersection(boundary),
            registrations: new Set(),
        };
        boundaries.set(boundary, boundaryState);
        intersectionObserver.observe(boundary);
        resizeObserver.observe(boundary);
    }

    const activeObserver = new MutationObserver(scheduleOwnershipUpdate);
    const registration = { activeObserver, boundary, boundaryState, region };
    registrations.set(region, registration);
    boundaryState.registrations.add(registration);
    activeObserver.observe(region, { attributes: true, attributeFilter: ["data-save-active"] });
    scheduleOwnershipUpdate();

    return {
        dispose() {
            activeObserver.disconnect();
            registrations.delete(region);
            boundaryState.registrations.delete(registration);
            region.dataset.saveVisible = "false";
            region.dataset.saveActive = "false";

            if (boundaryState.registrations.size === 0) {
                intersectionObserver.unobserve(boundary);
                resizeObserver.unobserve(boundary);
                boundaries.delete(boundary);
                boundary.dataset.stickySaveBoundaryActive = "false";
            }

            if (registrations.size > 0) {
                scheduleOwnershipUpdate();
            }
            stopCoordinator();
        },
    };
}
