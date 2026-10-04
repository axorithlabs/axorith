const posthogProjectToken = "phc_5JRZwyXJmWlMk1dln5MK3PipGNnAZNmRObSWLqjpMOT";
const posthogApiHost = "https://us.posthog.com";
const windowsInstallerPath = "/axorithlabs/axorith/releases/latest/download/axorith-setup.exe";

function normalizeWebsitePath(value) {
    if (typeof value !== "string") return "/other";

    try {
        const pathname = new URL(value, "https://axorith.com").pathname.replace(/\/+$/, "") || "/";
        const fixed = {
            "/": "/",
            "/index.html": "/",
            "/privacy": "/privacy",
            "/privacy.html": "/privacy",
            "/privacy/index.html": "/privacy",
            "/download": "/download",
            "/download/index.html": "/download",
            "/download/windows": "/download/windows",
            "/download/windows/index.html": "/download/windows",
            "/blog": "/blog",
            "/blog/index.html": "/blog"
        };

        if (fixed[pathname]) return fixed[pathname];

        const blogMatch = pathname.match(/^\/blog\/([a-z0-9-]+)(?:\/index\.html)?$/);
        if (guideMatch) return `/blog/${guideMatch[1]}`;

        return "/other";
    } catch {
        return "/other";
    }
}

function isWindowsInstallerDownloadUrl(value) {
    try {
        const url = new URL(value, "https://axorith.com");
        return url.origin === "https://github.com" && url.pathname === windowsInstallerPath;
    } catch {
        return false;
    }
}

function sanitizeWebsiteEvent(event) {
    const properties = event?.properties;
    if (!properties) return event;

    const sourceUrl = properties.$current_url;
    const referrerUrl = properties.$referrer || properties.$initial_referrer;
    let pagePath = "/other";
    let referrerSource;

    try {
        pagePath = normalizeWebsitePath(sourceUrl);
    } catch { }

    try {
        const host = new URL(referrerUrl).hostname.toLowerCase().replace(/^www\./, "");
        const sources = {
            "axorith.com": "internal",
            "google.com": "google",
            "bing.com": "bing",
            "duckduckgo.com": "duckduckgo",
            "yahoo.com": "yahoo",
            "github.com": "github",
            "reddit.com": "reddit",
            "linkedin.com": "linkedin",
            "facebook.com": "facebook",
            "instagram.com": "instagram",
            "youtube.com": "youtube",
            "x.com": "x",
            "twitter.com": "x",
            "chatgpt.com": "chatgpt",
            "perplexity.ai": "perplexity",
            "gemini.google.com": "gemini",
            "copilot.microsoft.com": "copilot"
        };
        referrerSource = Object.entries(sources).find(([domain]) =>
            host === domain || host.endsWith(`.${domain}`))?.[1] || "other";
    } catch { }

    for (const key of Object.keys(properties)) {
        if (key !== "$initial_pathname" && /(url|host|domain|referrer|pathname|path|gclid|fbclid|msclkid|\$ip)/i.test(key)) {
            delete properties[key];
        }
    }

    properties.$pathname = pagePath;
    if (typeof properties.$initial_pathname === "string") {
        properties.$initial_pathname = normalizeWebsitePath(properties.$initial_pathname);
    }
    if (referrerSource) properties.referrerSource = referrerSource;

    for (const key of ["$utm_source", "$utm_medium", "$utm_campaign"]) {
        const value = properties[key];
        if (typeof value === "string" && /^[a-z0-9_-]{1,32}$/i.test(value)) {
            properties[key] = value.toLowerCase();
        } else {
            delete properties[key];
        }
    }

    delete properties.$utm_content;
    delete properties.$utm_term;
    return event;
}

if (typeof window !== "undefined" && posthogProjectToken && !posthogProjectToken.startsWith("##")) {
    !function (document, posthog) {
        var methodNames = "capture register identify alias group set_group_properties resetGroups setPersonProperties setPersonPropertiesForFlags resetPersonPropertiesForFlags setGroupPropertiesForFlags resetGroupPropertiesForFlags startSessionRecording stopSessionRecording sessionRecordingStarted captureException loadToolbar getFeatureFlag getFeatureFlagPayload isFeatureEnabled reloadFeatureFlags updateEarlyAccessFeatureEnrollment getEarlyAccessFeatures on onFeatureFlags onSurveysLoaded getSurveys getActiveMatchingSurveys renderSurvey canLaunchSurvey getNextSurveyStep enable timeEvent get_property getSessionProperty".split(" ");
        var firstScript;

        if (posthog.__SV) return;
        window.posthog = posthog;
        posthog._i = [];
        posthog.init = function (token, config, name) {
            var methodIndex;
            var instanceName = name || "posthog";
            var instance = name ? posthog[name] = [] : posthog;
            var script = document.createElement("script");
            script.type = "text/javascript";
            script.crossOrigin = "anonymous";
            script.async = true;
            script.src = config.api_host.replace(".i.posthog.com", "-assets.i.posthog.com") + "/static/array.js";
            firstScript = document.getElementsByTagName("script")[0];
            firstScript.parentNode.insertBefore(script, firstScript);
            instance.people = instance.people || [];
            instance.toString = function (stub) {
                return "posthog" + (instanceName === "posthog" ? "" : "." + instanceName) + (stub ? " (stub)" : "");
            };
            instance.people.toString = function () { return instance.toString(true) + ".people (stub)"; };
            for (methodIndex = 0; methodIndex < methodNames.length; methodIndex++) {
                (function (method) {
                    instance[method] = function () {
                        instance.push([method].concat(Array.prototype.slice.call(arguments)));
                    };
                })(methodNames[methodIndex]);
            }
            posthog._i.push([token, config, instanceName]);
        };
        posthog.__SV = 1;
    }(document, window.posthog || []);

    window.posthog.init(posthogProjectToken, {
        api_host: posthogApiHost,
        defaults: "2026-05-30",
        autocapture: false,
        capture_pageview: true,
        capture_pageleave: true,
        capture_performance: true,
        before_send: sanitizeWebsiteEvent,
        loaded: function (posthog) {
            posthog.register({ application: "Axorith.Website" });
        }
    });

    window.posthog.register({ application: "Axorith.Website" });
}

if (typeof module !== "undefined") {
    module.exports = { sanitizeWebsiteEvent, normalizeWebsitePath, isWindowsInstallerDownloadUrl };
}