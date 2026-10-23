/** Reversible blocking UI; the page and its unsaved state remain in place. */
(function () {
    'use strict';
    if (globalThis.axorithSiteBlocker) return;

    const dialog = document.createElement('dialog');
    dialog.setAttribute('aria-label', 'Site blocked by Axorith');
    const styles = {
        display: 'block', position: 'fixed', inset: '0', margin: '0', padding: '0',
        width: '100vw', height: '100vh', 'max-width': 'none', 'max-height': 'none',
        border: '0', background: '#101010', overflow: 'hidden'
    };
    for (const [name, value] of Object.entries(styles)) dialog.style.setProperty(name, value, 'important');
    const frame = document.createElement('iframe');
    frame.src = (globalThis.browser ?? globalThis.chrome).runtime.getURL('blocked.html');
    frame.title = 'Axorith Site Blocker';
    frame.style.cssText = 'width:100%;height:100%;border:0;display:block';
    dialog.append(frame);
    dialog.addEventListener('cancel', event => event.preventDefault());

    const pauseMedia = event => {
        if (event.target instanceof HTMLMediaElement) event.target.pause();
    };
    document.addEventListener('play', pauseMedia, true);
    for (const media of document.querySelectorAll('audio, video')) media.pause();

    const focused = document.activeElement;
    document.documentElement.append(dialog);
    dialog.showModal();
    globalThis.axorithSiteBlocker = {
        remove() {
            document.removeEventListener('play', pauseMedia, true);
            dialog.close();
            dialog.remove();
            if (focused?.isConnected) focused.focus({ preventScroll: true });
            delete globalThis.axorithSiteBlocker;
        }
    };
})();
