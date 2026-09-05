(() => {
    const marker = document.currentScript.dataset.document;
    Blazor.start({
        ssr: { disableDomPreservation: document.currentScript.dataset.publicDocument === 'true' },
        circuit: {
            configureSignalR: builder => builder.withUrl(`_blazor?document=${encodeURIComponent(marker)}`),
        },
    });
})();
