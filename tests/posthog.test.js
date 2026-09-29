const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const test = require("node:test");
const { isWindowsInstallerDownloadUrl, normalizeWebsitePath, sanitizeWebsiteEvent } = require("../js/posthog.js");
const { configurePosthogScript } = require("../scripts/configure-posthog.js");

test("website deployment fills the PostHog browser config", () => {
    const source = readFileSync(require.resolve("../js/posthog.js"), "utf8");
    const configured = configurePosthogScript(source, "phc_testkey123", "https://us.i.posthog.com/");

    assert.match(configured, /const posthogProjectToken = "phc_testkey123";/);
    assert.match(configured, /const posthogApiHost = "https:\/\/us\.i\.posthog\.com";/);
    assert.equal(configured.includes("##POSTHOG_API_KEY##"), false);
});

test("homepage Download for Windows button links straight to the installer", () => {
    const homepage = readFileSync(require.resolve("../index.html"), "utf8");
    const button = homepage.match(/<a href="([^"]+)" class="btn btn-primary">\s*<svg[\s\S]*?<\/svg>\s*Download for Windows/);

    assert.ok(button, "homepage must contain its Download for Windows button");
    assert.equal(button[1], "https://github.com/axorithlabs/axorith/releases/latest/download/axorith-setup.exe");
});

test("website paths are normalized and limited to approved routes", () => {
    const event = {
        properties: {
            $current_url: "https://axorith.com/download/windows/?utm_source=docs",
            $initial_pathname: "/modules.html",
            $referrer: "https://google.com/search?q=axorith",
            $ip: "203.0.113.1",
            $utm_source: "Newsletter",
            $utm_term: "private-term"
        }
    };

    sanitizeWebsiteEvent(event);

    assert.equal(event.properties.$pathname, "/download/windows");
    assert.equal(event.properties.$initial_pathname, "/modules");
    assert.equal(event.properties.referrerSource, "google");
    assert.equal(event.properties.$utm_source, "newsletter");
    assert.equal("$current_url" in event.properties, false);
    assert.equal("$referrer" in event.properties, false);
    assert.equal("$ip" in event.properties, false);
    assert.equal("$utm_term" in event.properties, false);
    assert.equal(normalizeWebsitePath("https://axorith.com/download/"), "/download");
});

test("website download telemetry only recognizes the permanent installer asset", () => {
    const installer = "https://github.com/axorithlabs/axorith/releases/latest/download/axorith-setup.exe";

    assert.equal(isWindowsInstallerDownloadUrl(installer), true);
    assert.equal(isWindowsInstallerDownloadUrl("https://axorith.com/download/windows/"), false);
    assert.equal(isWindowsInstallerDownloadUrl("https://github.com/axorithlabs/axorith/releases/latest"), false);
    assert.equal(isWindowsInstallerDownloadUrl(installer.replace("https:", "http:")), false);
});
