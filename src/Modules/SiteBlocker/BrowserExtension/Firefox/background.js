/**
 * @file background.js
 * @description Background script for the Axorith Site Blocker extension (v2.2).
 * Supports BlockList/AllowList and automatic fallback between Dev/Prod native hosts.
 */

'use strict';

// --- Constants ---
const STORAGE_KEY_BLOCKED_DOMAINS = "axorith_blocked_domains";
const STORAGE_KEY_MODE = "axorith_blocking_mode";
const PROTOCOL_VERSION = 1;

// Host names to try. Priority: Dev -> Prod
const HOSTS = ["axorith.dev", "axorith"];
let currentHostIndex = 0;
let nativePort = null;

// --- Native Host Connection ---

function connectToHost() {
    const hostName = HOSTS[currentHostIndex];
    console.log(`Axorith: Attempting to connect to native host: ${hostName}`);
    
    const port = browser.runtime.connectNative(hostName);
    nativePort = port;
    
    // We need to detect immediate connection failures to switch hosts.
    // Native messaging doesn't give a clear "not found" error synchronously,
    // it usually fires onDisconnect immediately with an error.
    
    port.onMessage.addListener(handleNativeMessage);
    
    port.onDisconnect.addListener((p) => {
        if (p.error) {
            console.warn(`Axorith: Failed to connect/disconnected from ${hostName}: ${p.error.message}`);
            
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
        } else {
            // Clean disconnect (e.g. app closed). Retry same host after delay.
            console.log(`Axorith: Disconnected from ${hostName}. Retrying in 5s.`);
            nativePort = null;
            setTimeout(connectToHost, 5000);
        }
    });
}

async function handleNativeMessage(message) {
    console.log("Axorith: Received message from native host:", message);
    
    // If we receive a message, it means the connection is valid.
    // We can reset the index to prioritize this host next time (optional, but good for stability)
    // currentHostIndex = 0; // Actually, keep current index as it works.

    try {
        let blocking = false;
        if (message.command === "block" && Array.isArray(message.sites)) {
            blocking = await blockSites(message.sites, message.mode || "BlockList");
        } else if (message.command === "unblock") {
            await unblockSites();
        } else if (message.command === "health") {
            const state = await browser.storage.local.get([STORAGE_KEY_BLOCKED_DOMAINS, STORAGE_KEY_MODE]);
            blocking = sameSites(state[STORAGE_KEY_BLOCKED_DOMAINS], message.sites) &&
                state[STORAGE_KEY_MODE] === message.mode;
        }

        if (message.requestId && nativePort) {
            nativePort.postMessage({
                requestId: message.requestId,
                protocolVersion: PROTOCOL_VERSION,
                browser: "firefox",
                version: browser.runtime.getManifest().version,
                ok: message.command !== "block" || blocking,
                blocking
            });
        }
    } catch (error) {
        if (message.requestId && nativePort) {
            nativePort.postMessage({
                requestId: message.requestId,
                protocolVersion: PROTOCOL_VERSION,
                browser: "firefox",
                version: browser.runtime.getManifest().version,
                ok: false,
                status: "Error",
                message: error.message
            });
        }
        console.error("Axorith: Native request failed:", error);
    }
}


// --- Core Blocker Logic ---

async function blockSites(domains, mode) {
    if (domains.length === 0 && mode === "BlockList") {
        console.log("Axorith: Empty blocklist. Clearing blocks.");
        await unblockSites();
        return true;
    }

    console.log(`Axorith: Activating ${mode} for ${domains.length} domains.`);
    
    await browser.storage.local.set({ 
        [STORAGE_KEY_BLOCKED_DOMAINS]: domains,
        [STORAGE_KEY_MODE]: mode
    });

    const tabs = await browser.tabs.query({});
    const results = await Promise.all(tabs
        .filter(tab => shouldBlockUrl(tab.url, domains, mode))
        .map(tab => injectBlocker(tab.id)));
    return results.every(Boolean);
}

async function unblockSites() {
    console.log("Axorith: Deactivating all blocks.");
    
    const storage = await browser.storage.local.get([STORAGE_KEY_BLOCKED_DOMAINS, STORAGE_KEY_MODE]);
    const blockedDomains = storage[STORAGE_KEY_BLOCKED_DOMAINS];
    const mode = storage[STORAGE_KEY_MODE];
    
    if (!blockedDomains) {
        return;
    }

    await browser.storage.local.remove([STORAGE_KEY_BLOCKED_DOMAINS, STORAGE_KEY_MODE]);

    const tabs = await browser.tabs.query({});
    for (const tab of tabs) {
        if (shouldBlockUrl(tab.url, blockedDomains, mode)) {
            console.log(`Axorith: Reloading previously blocked tab ${tab.id} (${tab.url})`);
            browser.tabs.reload(tab.id).catch(e => console.warn(`Could not reload tab ${tab.id}: ${e.message}`));
        }
    }
}

function injectBlocker(tabId) {
    console.log(`Axorith: Injecting blocker into tab ${tabId}`);
    return browser.scripting.executeScript({
        target: { tabId: tabId },
        files: ["content.js"]
    }).then(() => true).catch(err => {
        console.warn(`Axorith: Failed to inject script into tab ${tabId}: ${err.message}. It might be a privileged page.`);
        return false;
    });
}

function sameSites(left, right) {
    if (!Array.isArray(left) || !Array.isArray(right) || left.length !== right.length) {
        return false;
    }
    const expected = new Set(right);
    return left.every(site => expected.has(site));
}


// --- Event Listeners ---

browser.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
    if (changeInfo.status !== 'complete' || !tab.url) {
        return;
    }

    const storage = await browser.storage.local.get([STORAGE_KEY_BLOCKED_DOMAINS, STORAGE_KEY_MODE]);
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
        
        const safeProtocols = ['about:', 'moz-extension:', 'chrome:', 'edge:', 'file:', 'view-source:'];
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
console.log("Axorith Background Script Loaded.");
connectToHost();
