const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const { normalizeWebsitePath, websiteLanguage, sanitizeWebsiteEvent, isWindowsInstallerDownloadUrl } = require('../js/posthog.js');

const languages = [...fs.readFileSync(path.join(__dirname, '../index.html'), 'utf8')
  .matchAll(/<link rel="alternate" hreflang="([^"]+)"/g)]
  .map(match => match[1]).filter(language => language !== 'x-default');
const blogPosts = fs.readdirSync(path.join(__dirname, '../blog'), { withFileTypes: true })
  .filter(entry => entry.isDirectory())
  .map(entry => [`/blog/${entry.name}/`, `/blog/${entry.name}`]);

test('every localized production page retains its category and actual page language', () => {
  for (const language of languages) {
    const prefix = language === 'en' ? '' : `/${language}`;
    for (const [page, category] of [['/', '/'], ['/download/', '/download'], ['/privacy/', '/privacy'], ['/blog/', '/blog'], ...blogPosts]) {
      const url = `https://axorith.com${prefix}${page}`;
      for (const current of [url, `${url}index.html?secret=value#private`]) {
        assert.equal(normalizeWebsitePath(current), category);
        assert.equal(websiteLanguage(current), language);
        const result = sanitizeWebsiteEvent({ event: '$pageview', properties: {
          $current_url: current, $initial_pathname: `${prefix}/download/`, $referrer: 'https://google.com/search?q=private',
          $host: 'private.invalid', $utm_term: 'private', $utm_content: 'private', $utm_source: 'GOOGLE',
          contentSlug: 'automate-work-setup-windows', platform: 'windows'
        } });
        assert.deepEqual(result.properties, {
          $pathname: category, $initial_pathname: '/download', pageLanguage: language, referrerSource: 'google',
          $utm_source: 'google', contentSlug: 'automate-work-setup-windows', platform: 'windows'
        });
      }
    }
  }
});

test('unknown and malformed URLs stay in the safe fallback category', () => {
  for (const url of [null, 'http://[', 'https://axorith.com/xx/private', 'https://axorith.com/rubbish/private']) {
    assert.equal(normalizeWebsitePath(url), '/other');
    assert.equal(websiteLanguage(url), 'en');
  }
  assert.equal(isWindowsInstallerDownloadUrl('https://github.com/axorithlabs/axorith/releases/latest/download/axorith-setup.exe'), true);
  assert.equal(isWindowsInstallerDownloadUrl('https://github.com.evil.invalid/axorithlabs/axorith/releases/latest/download/axorith-setup.exe'), false);
});

test('local previews do not load or send production analytics', () => {
  for (const hostname of ['localhost', '127.0.0.1', '', 'preview.example']) {
    const window = { location: { hostname } };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../js/posthog.js'), 'utf8'), { window, URL });
    assert.equal(window.posthog, undefined);
  }
});

test('real download and GitHub click handler keeps conversion payloads on localized pages', () => {
  const handlers = {};
  const captured = [];
  class Element {
    constructor(href) { this.href = href; this.dataset = { analyticsPlacement: 'article-end' }; }
    closest() { return this; }
  }
  const document = {
    documentElement: { classList: { add() {} } },
    body: { dataset: { contentSlug: 'automate-work-setup-windows', pageType: 'blog-post' } },
    addEventListener(name, handler) { handlers[name] = handler; },
    querySelector() { return null; }, querySelectorAll() { return []; }
  };
  const current = 'https://axorith.com/ru/blog/automate-work-setup-windows/';
  const window = { matchMedia() { return { matches: true }; }, posthog: {
    capture(event, properties) { captured.push(sanitizeWebsiteEvent({ event, properties: { ...properties, $current_url: current } })); }
  } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../js/main.js'), 'utf8'), {
    document, window, Element, URL, location: { href: current }, isWindowsInstallerDownloadUrl
  });
  handlers.DOMContentLoaded();
  handlers.click({ target: new Element('https://github.com/axorithlabs/axorith/releases/latest/download/axorith-setup.exe') });
  handlers.click({ target: new Element('https://github.com/axorithlabs/axorith') });
  assert.deepEqual(captured, [
    { event: 'WebsiteDownloadClicked', properties: { platform: 'windows', contentSlug: 'automate-work-setup-windows', contentType: 'blog-post', placement: 'article-end', $pathname: '/blog/automate-work-setup-windows', pageLanguage: 'ru' } },
    { event: 'WebsiteGitHubClicked', properties: { contentSlug: 'automate-work-setup-windows', contentType: 'blog-post', placement: 'article-end', $pathname: '/blog/automate-work-setup-windows', pageLanguage: 'ru' } }
  ]);
});
