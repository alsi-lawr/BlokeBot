export function openAuthPopup(url, name) {
    const width = 560;
    const height = 760;
    const left = Math.max(0, window.screenX + (window.outerWidth - width) / 2);
    const top = Math.max(0, window.screenY + (window.outerHeight - height) / 2);
    const features = [
        `width=${width}`,
        `height=${height}`,
        `left=${Math.round(left)}`,
        `top=${Math.round(top)}`,
        "popup=yes",
        "resizable=yes",
        "scrollbars=yes"
    ].join(",");

    const popup = window.open(url, name || "blokebot-oauth", features);
    if (!popup) {
        window.location.assign(url);
        return Promise.resolve(false);
    }

    popup.focus();

    return new Promise((resolve) => {
        const timer = window.setInterval(() => {
            if (popup.closed) {
                window.clearInterval(timer);
                resolve(true);
            }
        }, 400);
    });
}
