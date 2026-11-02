const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../src/Modules/SiteBlocker/BrowserExtension/Common/background.js'), 'utf8');

// Model the browser API boundary; execute the unchanged production background script.
function extension(firefox = false) {
    const storage = {};
    const calls = [];
    const replies = [];
    const hosts = [];
    const listeners = {};
    const api = {
        runtime: {
            getManifest: () => ({ version: 'test', ...(firefox ? { browser_specific_settings: { gecko: {} } } : {}) }),
            connectNative: host => {
                hosts.push(host);
                return {
                    onMessage: { addListener: callback => { listeners.message = callback; } },
                    onDisconnect: { addListener: callback => { listeners.disconnect = callback; } },
                    postMessage: reply => replies.push(reply)
                };
            }
        },
        storage: { local: {
            get: async keys => Object.fromEntries((Array.isArray(keys) ? keys : [keys]).map(key => [key, structuredClone(storage[key])])),
            set: async values => Object.assign(storage, structuredClone(values)),
            remove: async keys => keys.forEach(key => delete storage[key])
        } },
        tabs: {
            query: async () => [{ id: 1, url: 'https://blocked.example/page' }, { id: 2, url: 'https://allowed.example/' }],
            onUpdated: { addListener: callback => { listeners.updated = callback; } }
        },
        scripting: { executeScript: async options => { calls.push(options); if (api.failInjection && options.files) throw Error('injection denied'); } }
    };
    const context = vm.createContext({ [firefox ? 'browser' : 'chrome']: api, URL, setTimeout: () => 0, console: { log() {}, warn() {}, error() {} } });
    vm.runInContext(source, context);
    async function send(message) {
        const previousReplies = replies.length;
        listeners.message({ ...message, requestId: 'request-' + replies.length });
        await vm.runInContext('messageQueue', context);
        assert.equal(replies.length, previousReplies + 1, 'The native request must receive its own response');
        return replies.at(-1);
    }
    return { api, storage, calls, replies, hosts, listeners, context, send };
}

for (const firefox of [false, true]) {
    test(`${firefox ? 'Firefox Port.error' : 'Chromium runtime.lastError'} falls back to the production native host`, async () => {
        const app = extension(firefox);
        const error = { message: 'No such native application axorith.dev' };
        if (!firefox) app.api.runtime.lastError = error;
        app.listeners.disconnect(firefox ? { error } : {});
        delete app.api.runtime.lastError;
        await vm.runInContext('messageQueue', app.context);
        assert.deepEqual(app.hosts, ['axorith.dev', 'axorith']);
        assert.equal((await app.send({ command: 'block', sites: ['blocked.example'], mode: 'BlockList' })).blocking, true);
        assert.deepEqual(app.calls.filter(call => call.files).map(call => call.target.tabId), [1]);
    });
    test(`${firefox ? 'Firefox' : 'Chromium'} routes block, health, updates and unblock to the correct tabs`, async () => {
        const app = extension(firefox);
        assert.equal((await app.send({ command: 'block', sites: ['blocked.example'], mode: 'BlockList' })).blocking, true);
        assert.deepEqual(app.calls.filter(call => call.files).map(call => call.target.tabId), [1]);
        assert.equal((await app.send({ command: 'health', sites: ['blocked.example'], mode: 'BlockList' })).blocking, true);
        assert.equal((await app.send({ command: 'health', sites: ['different.example'], mode: 'BlockList' })).blocking, false);
        app.calls.length = 0;
        await app.listeners.updated(3, { status: 'complete' }, { url: 'https://sub.blocked.example/' });
        assert.equal(app.calls[0].target.tabId, 3);
        assert.deepEqual(Array.from(app.calls[0].files), ['content.js']);
        app.calls.length = 0;
        await app.send({ command: 'unblock' });
        assert.deepEqual(app.storage.axorith_blocking_rules, []);
        assert.equal(app.calls.filter(call => call.files).length, 0);
        assert.deepEqual(app.calls.map(call => call.target.tabId), [1, 2]);
    });
    for (const retain of [false, true]) {
        for (const disconnect of [false, true]) {
            test(`${firefox ? 'Firefox' : 'Chromium'} ${disconnect ? 'native disconnect' : 'host unavailable'} retains only committed rules (${retain})`, async () => {
                const app = extension(firefox);
                const rules = [{ sites: ['blocked.example'], mode: 'BlockList' }];
                await app.send({ command: 'sync', rules, retainOnDisconnect: retain });
                if (disconnect) {
                    app.listeners.disconnect();
                    await vm.runInContext('messageQueue', app.context);
                    vm.runInContext('connectToHost()', app.context);
                } else await app.send({ command: 'hostUnavailable' });
                assert.deepEqual(app.storage.axorith_blocking_rules, retain ? rules : []);
                assert.equal((await app.send({ command: 'health', sites: ['blocked.example'], mode: 'BlockList' })).blocking, retain);
            });
        }
    }
    test(`${firefox ? 'Firefox' : 'Chromium'} empty allowlist blocks web pages, excludes browser pages and reports failed injection`, async () => {
        const app = extension(firefox);
        assert.equal((await app.send({ command: 'block', sites: [], mode: 'AllowList' })).blocking, true);
        assert.deepEqual(app.calls.filter(call => call.files).map(call => call.target.tabId), [1, 2]);
        const before = app.calls.filter(call => call.files).length;
        for (const url of ['chrome://settings', 'about:blank', 'file:///private/document', 'moz-extension://id/page'])
            await app.listeners.updated(3, { status: 'complete' }, { url });
        assert.equal(app.calls.filter(call => call.files).length, before);
        app.api.failInjection = true;
        const failed = await app.send({ command: 'block', sites: ['blocked.example'], mode: 'BlockList' });
        assert.equal(failed.ok, false);
        assert.equal(failed.blocking, false);
    });
}
