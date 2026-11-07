export const isMac = /Mac|iPhone|iPad/.test(navigator.userAgentData?.platform || navigator.platform || '');
export const hasDirectoryAccess = typeof window.showDirectoryPicker === 'function';
export const additive = event => isMac ? event.metaKey : event.ctrlKey || event.metaKey;
export const shortcut = text => isMac ? text.replaceAll('Ctrl+', '⌘').replaceAll('Shift+', '⇧').replaceAll('Alt+', '⌥') : text;
export function modifierHelp(text) { return isMac ? text.replaceAll('Ctrl', 'Command') : text; }
