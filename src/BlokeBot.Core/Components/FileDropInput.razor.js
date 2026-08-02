const registrations = new Map();
let dragDepth = 0;
let listenersInstalled = false;

const hasFiles = (event) =>
    Array.from(event.dataTransfer?.types ?? []).includes("Files");

const registrationFromEvent = (event) => {
    for (const element of event.composedPath()) {
        const registration = registrations.get(element);
        if (registration !== undefined && !registration.disabled) {
            return registration;
        }
    }
    return null;
};

const showDimmer = () => {
    document.body.classList.add("blokebot-file-drag-active");
};

const clearDragState = () => {
    dragDepth = 0;
    document.body.classList.remove("blokebot-file-drag-active");
};

const onDragEnter = (event) => {
    if (!hasFiles(event)) {
        return;
    }

    event.preventDefault();
    dragDepth += 1;
    showDimmer();
};

const onDragOver = (event) => {
    if (!hasFiles(event)) {
        return;
    }

    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    showDimmer();
};

const onDragLeave = (event) => {
    if (!hasFiles(event)) {
        return;
    }

    if (event.relatedTarget === null) {
        clearDragState();
        return;
    }

    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) {
        clearDragState();
    }
};

const onDrop = (event) => {
    if (!hasFiles(event)) {
        return;
    }

    event.preventDefault();
    const registration = registrationFromEvent(event);
    const files = event.dataTransfer.files;
    clearDragState();
    if (registration === null || files.length === 0) {
        return;
    }

    const transfer = new DataTransfer();
    transfer.items.add(files[0]);
    registration.input.files = transfer.files;
    registration.input.dispatchEvent(new Event("change", { bubbles: true }));
};

const installListeners = () => {
    if (listenersInstalled) {
        return;
    }

    listenersInstalled = true;
    document.addEventListener("dragenter", onDragEnter, true);
    document.addEventListener("dragover", onDragOver, true);
    document.addEventListener("dragleave", onDragLeave, true);
    document.addEventListener("drop", onDrop, true);
    document.addEventListener("dragend", clearDragState, true);
    window.addEventListener("blur", clearDragState);
};

const removeListeners = () => {
    if (!listenersInstalled) {
        return;
    }

    listenersInstalled = false;
    clearDragState();
    document.removeEventListener("dragenter", onDragEnter, true);
    document.removeEventListener("dragover", onDragOver, true);
    document.removeEventListener("dragleave", onDragLeave, true);
    document.removeEventListener("drop", onDrop, true);
    document.removeEventListener("dragend", clearDragState, true);
    window.removeEventListener("blur", clearDragState);
};

export function bindFileDrop(root, disabled) {
    const input = root.querySelector('input[type="file"]');
    if (!(input instanceof HTMLInputElement)) {
        throw new Error("File drop input is unavailable.");
    }

    const registration = { input, disabled };
    registrations.set(root, registration);
    installListeners();

    return {
        browse: () => {
            if (!registration.disabled) {
                input.click();
            }
        },
        setDisabled: (value) => {
            registration.disabled = value;
        },
        dispose: () => {
            registrations.delete(root);
            if (registrations.size === 0) {
                removeListeners();
            }
        },
    };
}
