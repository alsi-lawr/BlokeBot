export function bindEditorActionTooltips(document) {
    const window = document.defaultView;
    const lifetime = new AbortController();
    const options = { signal: lifetime.signal };
    const ownerOf = target => target?.closest?.('[data-editor-action-tooltip]');
    let active = null;

    function dismiss() {
        if (active === null) return;
        const { tooltip, events, observer, sizes } = active;
        active = null;
        events.abort();
        observer.disconnect();
        sizes.disconnect();
        if (tooltip.matches(':popover-open')) tooltip.hidePopover();
    }

    function position() {
        if (active === null) return;
        const { owner, tooltip } = active;
        if (!owner.isConnected || !tooltip.isConnected || owner.getClientRects().length === 0) {
            dismiss();
            return;
        }
        const viewport = window.visualViewport;
        const left = (viewport?.offsetLeft ?? 0) + 8;
        const top = (viewport?.offsetTop ?? 0) + 8;
        const right = left + (viewport?.width ?? document.documentElement.clientWidth) - 16;
        const bottom = top + (viewport?.height ?? window.innerHeight) - 16;
        const anchor = owner.getBoundingClientRect();
        const tip = tooltip.getBoundingClientRect();
        const x = Math.max(left, Math.min(anchor.right - tip.width, right - tip.width));
        const preferredY = anchor.bottom + 6;
        const y = Math.max(top, Math.min(
            preferredY + tip.height <= bottom ? preferredY : anchor.top - tip.height - 6,
            bottom - tip.height
        ));
        const inset = `${y}px auto auto ${x}px`;
        if (tooltip.style.inset !== inset) tooltip.style.inset = inset;
    }

    function show(owner) {
        if (!owner || active?.owner === owner) return;
        dismiss();
        if (!owner.isConnected) return;
        const tooltip = owner.querySelector('[role="tooltip"]');
        owner.removeAttribute('data-tip-dismissed');
        const events = new AbortController();
        const observer = new window.MutationObserver(position);
        const sizes = new window.ResizeObserver(position);
        active = { owner, tooltip, events, observer, sizes };
        tooltip.showPopover();
        position();
        if (active === null) return;
        observer.observe(document.documentElement, {
            subtree: true, childList: true, attributes: true,
            attributeFilter: ['hidden', 'class', 'style']
        });
        sizes.observe(owner);
        sizes.observe(tooltip);
        const visibleOptions = { signal: events.signal };
        document.addEventListener('scroll', dismiss, { ...visibleOptions, capture: true });
        window.addEventListener('resize', dismiss, visibleOptions);
        window.addEventListener('blur', dismiss, visibleOptions);
        window.visualViewport?.addEventListener('scroll', dismiss, visibleOptions);
        window.visualViewport?.addEventListener('resize', dismiss, visibleOptions);
    }

    function depart(event) {
        const owner = ownerOf(event.target);
        if (owner === active?.owner && !owner.contains(event.relatedTarget)
            && !owner.matches(':hover, :focus-within')) dismiss();
    }

    document.addEventListener('pointerenter', event => {
        const owner = ownerOf(event.target);
        if (owner === event.target) show(owner);
    }, { ...options, capture: true });
    document.addEventListener('focusin', event => show(ownerOf(event.target)), options);
    document.addEventListener('pointerleave', event => {
        if (ownerOf(event.target) === event.target) depart(event);
    }, { ...options, capture: true });
    document.addEventListener('focusout', depart, options);
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        if (active !== null) active.owner.dataset.tipDismissed = 'true';
        dismiss();
    }, options);

    return { dispose() { dismiss(); lifetime.abort(); } };
}
