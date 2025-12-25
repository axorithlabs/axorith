/**
 * @file background.js
 * @description Background service worker for the Axorith Site Blocker Chrome extension (v2.2).
 * Supports BlockList/AllowList and automatic fallback between Dev/Prod native hosts.
 */

'use strict';

// --- Constants ---
const STORAGE_KEY_BLOCKED_DOMAINS = "axorith_blocked_domains";
const STORAGE_KEY_MODE = "axorith_blocking_mode";

// Host names to try. Priority: Dev -> Prod
const HOSTS = ["axorith.dev", "axorith"];
let currentHostIndex = 0;
let nativePort = null;

// --- Native Host Connection ---

function connectToHost() {
    const hostName = HOSTS[currentHostIndex];
    console.log(`Axorith: Attempting to connect to native host: ${hostName}`);
    
    try {
        nativePort = chrome.runtime.connectNative(hostName);
    } catch (e) {
        console.error(`Axorith: Failed to initiate connection to ${hostName}:`, e);
        handleConnectionFailure();
        return;
    }
    
    nativePort.onMessage.addListener(handleNativeMessage);
    
    nativePort.onDisconnect.addListener(() => {
        const error = chrome.runtime.lastError;
        if (error) {
            console.warn(`Axorith: Failed to connect/disconnected from ${hostName}: ${error.message}`);
            handleConnectionFailure();
        } else {
            // Clean disconnect (e.g. app closed). Retry same host after delay.
            console.log(`Axorith: Disconnected from ${hostName}. Retrying in 5s.`);
            nativePort = null;
            setTimeout(connectToHost, 5000);
        }
    });
}

function handleConnectionFailure() {
    nativePort = null;
    
    // If we haven't tried all hosts yet, try the next one immediately
    if (currentHostIndex < HOSTS.length - 1) {
        currentHostIndex++;
        console.log("Axorith: Switching to next host configuration...");
        connectToHost();
    } else {
        // We exhausted all options. Reset index and retry after delay.
        console.error("Axorith: All host connection attempts failed.");
        currentHostIndex = 0;
        console.log("Axorith: Will retry from start in 5 seconds.");
        setTimeout(connectToHost, 5000);
    }
}

function handleNativeMessage(message) {
    console.log("Axorith: Received message from native host:", message);

    if (message.command === "block" && Array.isArray(message.sites)) {
        const mode = message.mode || "BlockList";
        blockSites(message.sites, mode);
    } else if (message.command === "unblock") {
        unblockSites();
    }
}


// --- Core Blocker Logic ---

async function blockSites(domains, mode) {
    if (domains.length === 0 && mode === "BlockList") {
        console.log("Axorith: Empty blocklist. Clearing blocks.");
        await unblockSites();
        return;
    }

    console.log(`Axorith: Activating ${mode} for ${domains.length} domains.`);
    
    await chrome.storage.local.set({ 
        [STORAGE_KEY_BLOCKED_DOMAINS]: domains,
        [STORAGE_KEY_MODE]: mode
    });

    const tabs = await chrome.tabs.query({});
    for (const tab of tabs) {
        if (shouldBlockUrl(tab.url, domains, mode)) {
            injectBlocker(tab.id);
        }
    }
}

async function unblockSites() {
    console.log("Axorith: Deactivating all blocks.");
    
    const storage = await chrome.storage.local.get([STORAGE_KEY_BLOCKED_DOMAINS, STORAGE_KEY_MODE]);
    const blockedDomains = storage[STORAGE_KEY_BLOCKED_DOMAINS];
    const mode = storage[STORAGE_KEY_MODE];
    
    if (!blockedDomains) {
        return;
    }

    await chrome.storage.local.remove([STORAGE_KEY_BLOCKED_DOMAINS, STORAGE_KEY_MODE]);

    const tabs = await chrome.tabs.query({});
    for (const tab of tabs) {
        if (shouldBlockUrl(tab.url, blockedDomains, mode)) {
            console.log(`Axorith: Reloading previously blocked tab ${tab.id} (${tab.url})`);
            chrome.tabs.reload(tab.id).catch(e => console.warn(`Could not reload tab ${tab.id}: ${e.message}`));
        }
    }
}

function injectBlocker(tabId) {
    console.log(`Axorith: Injecting blocker into tab ${tabId}`);
    chrome.scripting.executeScript({
        target: { tabId: tabId },
        files: ["content.js"]
    }).catch(err => console.warn(`Axorith: Failed to inject script into tab ${tabId}: ${err.message}. It might be a privileged page.`));
}


// --- Event Listeners ---

chrome.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
    if (changeInfo.status !== 'complete' || !tab.url) {
        return;
    }

    const storage = await chrome.storage.local.get([STORAGE_KEY_BLOCKED_DOMAINS, STORAGE_KEY_MODE]);
    const domains = storage[STORAGE_KEY_BLOCKED_DOMAINS];
    const mode = storage[STORAGE_KEY_MODE];

    if (domains && shouldBlockUrl(tab.url, domains, mode)) {
        injectBlocker(tabId);
    }
});


// --- Utility Functions ---

function shouldBlockUrl(urlString, domainList, mode) {
    if (!urlString || !domainList) {
        return false;
    }

    try {
        const url = new URL(urlString);
        
        // Chrome-specific protocols to ignore
        const safeProtocols = ['chrome:', 'chrome-extension:', 'edge:', 'about:', 'file:', 'view-source:', 'devtools:'];
        if (safeProtocols.some(proto => url.protocol.startsWith(proto))) {
            return false;
        }

        const isMatch = domainList.some(domain => {
            return url.hostname === domain || url.hostname.endsWith('.' + domain);
        });

        if (mode === "AllowList") {
            return !isMatch;
        } else {
            return isMatch;
        }

    } catch (e) {
        return false;
    }
}

// --- Initialization ---
console.log("Axorith Background Service Worker Loaded.");
connectToHost();
