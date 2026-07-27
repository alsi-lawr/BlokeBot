const handledKeys = new Set(["ArrowRight", "ArrowLeft", "Home", "End"]);

export function bindTabKeys(tabList, component) {
    const onKeyDown = (event) => {
        if (!handledKeys.has(event.key)) {
            return;
        }

        event.preventDefault();
        void component.invokeMethodAsync("HandleTabKeyAsync", event.key);
    };

    tabList.addEventListener("keydown", onKeyDown);
    return {
        dispose: () => tabList.removeEventListener("keydown", onKeyDown),
    };
}
