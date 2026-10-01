const fs = require("node:fs");
const path = require("node:path");

function configurePosthogScript(source, key, host = "https://us.i.posthog.com") {
    if (!/^[A-Za-z0-9_-]{8,128}$/.test(key || "")) {
        throw new Error("POSTHOG_API_KEY is missing or invalid.");
    }

    host = host?.trim() || "https://us.i.posthog.com";
    if (!/^https:\/\/[A-Za-z0-9.-]+(?::[0-9]+)?(?:\/[A-Za-z0-9._~/-]*)?\/?$/.test(host)) {
        throw new Error("POSTHOG_API_HOST must be an HTTPS URL.");
    }
    host = host.replace(/\/+$/, "");

    const keyPattern = /const posthogProjectToken = "[^"]+";/g;
    const hostPattern = /const posthogApiHost = "[^"]+";/g;
    if (source.match(keyPattern)?.length !== 1 || source.match(hostPattern)?.length !== 1) {
        throw new Error("PostHog defaults were not found exactly once.");
    }

    return source
        .replace(keyPattern, `const posthogProjectToken = "${key}";`)
        .replace(hostPattern, `const posthogApiHost = "${host}";`);
}

if (require.main === module) {
    const file = path.join(__dirname, "../js/posthog.js");
    const source = fs.readFileSync(file, "utf8");
    const configured = configurePosthogScript(source, process.env.POSTHOG_API_KEY, process.env.POSTHOG_API_HOST);
    fs.writeFileSync(file, configured, "utf8");
    console.log("Configured website PostHog client.");
}

module.exports = { configurePosthogScript };