/**
 * @file content.js
 * @description This script is injected into tabs that are designated for blocking.
 * Its primary responsibility is to immediately halt all page activity (scripts, media)
 * and replace the page's content with the Axorith blocker UI, including its styles.
 */

(async function() {
    'use strict';

    // --- Main Execution ---

    // Immediately stop everything. This is critical to prevent audio/video from playing
    // and further scripts from executing.
    window.stop();

    // Fetch the blocker page content from the extension's resources.
    const blockerUrl = browser.runtime.getURL('blocked.html');
    let blockerHtml;
    try {
        const response = await fetch(blockerUrl);
        if (!response.ok) {
            console.error(`Axorith: Failed to fetch blocker HTML. Status: ${response.status}`);
            return;
        }
        blockerHtml = await response.text();
    } catch (error) {
        console.error('Axorith: Error fetching blocker HTML.', error);
        return;
    }

    // Parse the fetched HTML to extract both styles and body content.
    const parser = new DOMParser();
    const doc = parser.parseFromString(blockerHtml, 'text/html');
    
    // Extract styles
    const styles = doc.head.querySelector('style')?.textContent;
    
    // --- DOM Manipulation ---

    // 1. Clear the existing document content to remove conflicting styles and scripts.
    // Using replaceChildren() is safer and faster than innerHTML = ''
    document.head.replaceChildren();
    document.body.replaceChildren();

    // 2. Inject the styles into the now-empty head.
    if (styles) {
        const styleElement = document.createElement('style');
        styleElement.textContent = styles;
        document.head.appendChild(styleElement);
    }

    // 3. Inject the blocker UI into the now-empty body.
    // Instead of innerHTML, we move the nodes from the parsed document.
    // This satisfies the "Unsafe assignment" warning.
    const newBodyNodes = Array.from(doc.body.children);
    document.body.append(...newBodyNodes);

})();