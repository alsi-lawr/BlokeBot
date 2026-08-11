export function register(region) {
    const boundary = region.closest("[data-sticky-save-scope]") ?? region.parentElement;
    if (!boundary) {
        return { dispose() {} };
    }

    const updateBoundary = () => {
        const active = [...boundary.querySelectorAll(".sticky-save-region[data-save-active='true']")].some(
            (candidate) =>
                (candidate.closest("[data-sticky-save-scope]") ?? candidate.parentElement) === boundary,
        );
        boundary.dataset.stickySaveBoundaryActive = String(active);
    };
    const update = (intersects) => {
        const active = region.dataset.saveActive === "true";
        region.dataset.saveVisible = String(active && intersects);
        updateBoundary();
    };
    let intersects = false;
    const intersectionObserver = new IntersectionObserver(
        (entries) => {
            intersects = entries.some((entry) => entry.isIntersecting);
            update(intersects);
        },
        { rootMargin: "-1px 0px -1px 0px" },
    );
    const mutationObserver = new MutationObserver(() => update(intersects));

    intersectionObserver.observe(boundary);
    mutationObserver.observe(region, { attributes: true, attributeFilter: ["data-save-active"] });

    return {
        dispose() {
            intersectionObserver.disconnect();
            mutationObserver.disconnect();
            region.dataset.saveVisible = "false";
            region.dataset.saveActive = "false";
            updateBoundary();
        },
    };
}
