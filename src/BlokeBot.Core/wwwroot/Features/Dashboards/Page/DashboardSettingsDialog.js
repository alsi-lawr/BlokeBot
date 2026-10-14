export function createSettingsDialog(root, dialog, dotnet, report) {
  let current = null, dismissed = null, disposed = false;
  const owns = owner => !disposed && root.isConnected && root.dataset.settingsOwner === owner;
  const visible = node => node?.isConnected && !node.disabled && node.getClientRects().length > 0 && !node.closest('[hidden],[inert]');
  const restore = id => {
    const gear = root.querySelector(`[data-settings-for="${id}"]`);
    const card = root.querySelector(`[data-widget-id="${id}"]`);
    const target = visible(gear) ? gear : visible(card) ? card : root.querySelector('[data-dashboard-grid]');
    if (visible(target)) target.focus({ preventScroll: true });
  };
  const hide = returnFocus => {
    const previous = current;
    current = null;
    if (dialog.open) dialog.close();
    if (previous && returnFocus) restore(previous.id);
  };
  const closed = () => {
    const previous = current;
    if (!previous || !owns(previous.owner)) return;
    dismissed = previous.owner;
    hide(true);
    void dotnet.invokeMethodAsync('SettingsClosedAsync', previous.owner).catch(report);
  };
  const cancel = event => { event.preventDefault(); closed(); };
  const nativeClose = () => { if (!dialog.open) closed(); };
  dialog.addEventListener('cancel', cancel);
  dialog.addEventListener('close', nativeClose);
  return {
    sync(owner, id) {
      if (!owns(owner)) return;
      if (!id || owner === dismissed) { hide(true); return; }
      if (current?.id !== id) hide(false);
      current = { owner, id };
      if (!dialog.open) dialog.showModal();
    },
    focus(owner, position) {
      if (!owns(owner) || current?.owner !== owner || !dialog.open) return;
      if (position) {
        const details = dialog.querySelector('.dashboard-placement');
        if (details) details.open = true;
      }
      const target = position ? dialog.querySelector('[data-placement-field="Width"]') : dialog.querySelector('input,select');
      target?.focus({ preventScroll: true });
    },
    dispose() {
      disposed = true;
      hide(false);
      dialog.removeEventListener('cancel', cancel);
      dialog.removeEventListener('close', nativeClose);
    },
  };
}
