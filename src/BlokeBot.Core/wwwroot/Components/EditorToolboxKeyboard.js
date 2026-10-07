// Toolbox rows are insertion controls, not document-history targets. Inputs retain native editing.
export function containToolboxKeyboard(event) {
    if (!event.target?.closest?.('[data-editor-toolbox]')) return false;
    const editable = event.target.closest('input,textarea,select,[contenteditable=true]');
    const key = event.key.toLowerCase();
    if (!editable && (event.key === 'Delete' || (!event.altKey && (event.ctrlKey || event.metaKey) && (key === 'z' || key === 'y')))) {
        event.preventDefault();
    }
    return true;
}
