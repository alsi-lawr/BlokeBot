const registrations = new Map();
const supportedFragments = new Set(["#bot-status", "#chat-tools", "#moderator-help"]);

export function observe(rootId, reference) {
    dispose(rootId);

    let notificationVersion = 0;
    const notify = () => {
        notificationVersion++;
        reference
            .invokeMethodAsync("NotifyFragmentChangedAsync", window.location.href)
            .catch(() => dispose(rootId));
    };

    const hashHandler = () => notify();
    const clickHandler = (event) => {
        const anchor = event.target instanceof Element ? event.target.closest("a[href]") : null;
        if (anchor === null)
            return;

        const target = new URL(anchor.href, window.location.href);
        if (
            target.origin !== window.location.origin ||
            target.pathname !== window.location.pathname ||
            target.search !== window.location.search ||
            !supportedFragments.has(target.hash)
        ) {
            return;
        }

        const versionBeforeNavigation = notificationVersion;
        window.setTimeout(() => {
            if (
                notificationVersion === versionBeforeNavigation &&
                window.location.hash === target.hash
            ) {
                notify();
            }
        }, 0);
    };

    window.addEventListener("hashchange", hashHandler);
    window.addEventListener("click", clickHandler);
    registrations.set(rootId, { clickHandler, hashHandler });
}

export function dispose(rootId) {
    const registration = registrations.get(rootId);
    if (registration === undefined)
        return;

    window.removeEventListener("hashchange", registration.hashHandler);
    window.removeEventListener("click", registration.clickHandler);
    registrations.delete(rootId);
}
