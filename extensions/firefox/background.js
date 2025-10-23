/**
 * @file background.js
 * @description Background script for the Axorith Site Blocker extension (v2).
 * This version uses a script injection model instead of redirection.
 * It listens to the native host, manages a list of blocked domains in storage,
 * and injects a content script to take over pages on matching tabs.
 * When unblocked, it reloads the tabs to restore their original content.
 */

'use strict';

// --- Constants ---
const STORAGE_KEY_BLOCKED_DOMAINS = "axorith_blocked_domains";

// --- Native Host Connection ---

/**
 * Establishes and manages a persistent connection to the native messaging host.
 * Automatically attempts to reconnect on disconnection.
 */
function connectToHost() {
    const port = browser.runtime.connectNative("axorith");
    console.log("Axorith: Connecting to native host...");
    port.onMessage.addListener(handleNativeMessage);
    port.onDisconnect.addListener(onDisconnect);
}

/**
 * Handles incoming messages from the native C# application.
 * @param {object} message - The message object received from the host.
 */
function handleNativeMessage(message) {
    console.log("Axorith: Received message from native host:", message);
    if (message.command === "block" && Array.isArray(message.sites)) {
        blockSites(message.sites);
    } else if (message.command === "unblock") {
        unblockSites();
    }
}

/**
 * Handles the disconnection event from the native host port.
 * Logs the error and schedules a reconnection attempt.
 * @param {object} p - The port object that was disconnected.
 */
function onDisconnect(p) {
    if (p.error) {
        console.error(`Axorith: Disconnected from native host: ${p.error.message}`);
    } else {
        console.log("Axorith: Disconnected from native host.");
    }
    console.log("Axorith: Will attempt to reconnect in 5 seconds.");
    setTimeout(connectToHost, 5000);
}


// --- Core Blocker Logic ---

/**
 * Activates blocking for a given list of domains.
 * 1. Stores the list of domains in local storage.
 * 2. Queries all existing tabs and injects the blocker into matching ones.
 * @param {string[]} domains - An array of domain strings to block.
 */
async function blockSites(domains) {
    if (domains.length === 0) {
        console.log("Axorith: Received block command with no domains. Clearing blocks.");
        await unblockSites();
        return;
    }

    console.log(`Axorith: Activating block for domains: ${domains.join(', ')}`);
    await browser.storage.local.set({ [STORAGE_KEY_BLOCKED_DOMAINS]: domains });

    const tabs = await browser.tabs.query({});
    for (const tab of tabs) {
        if (isDomainBlocked(tab.url, domains)) {
            injectBlocker(tab.id);
        }
    }
}

/**
 * Deactivates blocking.
 * 1. Clears the list of blocked domains from storage.
 * 2. Queries all tabs and reloads any that are currently blocked to restore them.
 */
async function unblockSites() {
    console.log("Axorith: Deactivating all blocks.");
    const { [STORAGE_KEY_BLOCKED_DOMAINS]: blockedDomains } = await browser.storage.local.get(STORAGE_KEY_BLOCKED_DOMAINS);
    
    if (!blockedDomains || blockedDomains.length === 0) {
        return; // Nothing to unblock.
    }

    await browser.storage.local.remove(STORAGE_KEY_BLOCKED_DOMAINS);

    const tabs = await browser.tabs.query({});
    for (const tab of tabs) {
        // A tab needs to be reloaded if its URL matches a previously blocked domain.
        if (isDomainBlocked(tab.url, blockedDomains)) {
            console.log(`Axorith: Reloading previously blocked tab ${tab.id} (${tab.url})`);
            browser.tabs.reload(tab.id).catch(e => console.warn(`Could not reload tab ${tab.id}: ${e.message}`));
        }
    }
}

/**
 * Injects the content script into a specific tab to block its content.
 * @param {number} tabId - The ID of the tab to block.
 */
function injectBlocker(tabId) {
    console.log(`Axorith: Injecting blocker into tab ${tabId}`);
    browser.scripting.executeScript({
        target: { tabId: tabId },
        files: ["content.js"]
    }).catch(err => console.warn(`Axorith: Failed to inject script into tab ${tabId}: ${err.message}. It might be a privileged page.`));
}


// --- Event Listeners ---

/**
 * Listens for tab updates (e.g., navigation).
 * If a tab navigates to a blocked domain, inject the blocker.
 * This handles cases where a tab is opened or navigated after the block is active.
 */
browser.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
    // We only care about tabs that have finished loading their URL.
    if (changeInfo.status !== 'complete' || !tab.url) {
        return;
    }

    const { [STORAGE_KEY_BLOCKED_DOMAINS]: domains } = await browser.storage.local.get(STORAGE_KEY_BLOCKED_DOMAINS);
    if (domains && domains.length > 0 && isDomainBlocked(tab.url, domains)) {
        injectBlocker(tabId);
    }
});


// --- Utility Functions ---

/**
 * Checks if a given URL belongs to any of the blocked domains.
 * @param {string | undefined} urlString - The URL to check.
 * @param {string[]} blockedDomains - The list of domains to check against.
 * @returns {boolean} - True if the URL is blocked, false otherwise.
 */
function isDomainBlocked(urlString, blockedDomains) {
    if (!urlString) {
        return false;
    }
    try {
        const url = new URL(urlString);
        // Check against special browser/extension protocols
        if (['about:', 'moz-extension:', 'chrome:', 'file:'].some(proto => url.protocol.startsWith(proto))) {
            return false;
        }
        return blockedDomains.some(blockedDomain => url.hostname.endsWith(blockedDomain));
    } catch (e) {
        // Invalid URL
        return false;
    }
}

// --- Initialization ---
console.log("Axorith Background Script Loaded.");
connectToHost();
