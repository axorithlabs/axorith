"""Render crawlable translations with Python's standard library.

Edit the English HTML and locales/*.json, then run python scripts/localize.py.
No browser translation, redirects, dependencies, or runtime build step.
"""

import argparse
import json
import re
from html import escape
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urljoin, urlsplit

ROOT = Path(__file__).resolve().parents[1]
ORIGIN = "https://axorith.com"
LANGUAGES = {
    "en": ("English", "en_US"),
    "ru": ("Русский", "ru_RU"),
    "zh-Hans": ("简体中文", "zh_CN"),
    "zh-Hant": ("繁體中文", "zh_TW"),
    "fr": ("Français", "fr_FR"),
    "pt": ("Português", "pt_BR"),
    "es": ("Español", "es_ES"),
    "de": ("Deutsch", "de_DE"),
    "ja": ("日本語", "ja_JP"),
    "nl": ("Nederlands", "nl_NL"),
    "pl": ("Polski", "pl_PL"),
}
NAMES = {"Axorith", "Axorith Labs", "GitHub", "CLion", "Spotify", "Steam",
         "Discord", "Home Assistant", "Cold Turkey Blocker", "Freedom", "Windows",
         "mail@axorith.com"}
JSON_TEXT = {"name", "headline", "description", "applicationSubCategory"}


def sources():
    return [ROOT / "index.html", *sorted((ROOT / "blog").rglob("index.html")),
            ROOT / "download/index.html", ROOT / "privacy/index.html"]


def page_path(file):
    return "/" + file.relative_to(ROOT).as_posix().removesuffix("index.html")


def localized(path, language):
    return path if language == "en" else f"/{language}{path}"


def clean_generated(source):
    return re.sub(r"\n?<!-- locales:start -->.*?<!-- locales:end -->\n?", "", source, flags=re.S)


class Renderer(HTMLParser):
    def __init__(self, path, language="en", catalog=None):
        super().__init__(convert_charrefs=True)
        self.path, self.language, self.catalog = path, language, catalog
        self.output, self.strings = [], set()
        self.script_type = None

    def translate(self, text):
        value = text.strip()
        if not re.search(r"[A-Za-z]", value) or value in NAMES:
            return text
        self.strings.add(value)
        if self.catalog is None:
            return text
        if not self.catalog.get(value, "").strip():
            raise ValueError(f"{self.language}: missing translation: {value}")
        return text[:len(text) - len(text.lstrip())] + self.catalog[value] + text[len(text.rstrip()):]

    def internal_url(self, value, asset=False):
        url = urlsplit(urljoin(ORIGIN + self.path, value))
        if url.netloc != "axorith.com" or url.scheme != "https":
            return value
        path = url.path
        if asset or path.startswith(("/assets/", "/css/", "/js/")):
            return path
        path = path.removesuffix("index.html")
        result = localized(path, self.language)
        return result + ("?" + url.query if url.query else "") + ("#" + url.fragment if url.fragment else "")

    def handle_decl(self, decl):
        self.output.append(f"<!{decl}>")

    def handle_comment(self, text):
        self.output.append(f"<!--{text}-->")

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        if tag in ("script", "style"):
            self.script_type = attributes.get("type", tag)
        changed = {}
        for key, value in attrs:
            if value is None:
                continue
            if key in ("aria-label", "alt", "title") or key == "content" and (
                attributes.get("name") in ("description", "twitter:title", "twitter:description")
                or attributes.get("property") in ("og:title", "og:description")
            ):
                changed[key] = self.translate(value)
            elif key in ("href", "src"):
                changed[key] = self.internal_url(value, asset=key == "src")
            elif tag == "meta" and attributes.get("property") == "og:url" and key == "content":
                changed[key] = ORIGIN + localized(self.path, self.language)
        if tag == "html":
            changed["lang"] = self.language
        if tag == "link" and attributes.get("rel") == "canonical":
            changed["href"] = ORIGIN + localized(self.path, self.language)
        raw = self.get_starttag_text()
        for key, value in changed.items():
            raw = re.sub(rf'(\b{re.escape(key)}\s*=\s*)([\"\']).*?\2',
                         lambda m: m[1] + '"' + escape(value, quote=True) + '"', raw, flags=re.S)
        self.output.append(raw)

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag in ("script", "style"):
            self.script_type = None

    def handle_endtag(self, tag):
        if tag in ("script", "style"):
            self.script_type = None
        self.output.append(f"</{tag}>")

    def json_value(self, value, key=""):
        if isinstance(value, dict):
            result = {k: self.json_value(v, k) for k, v in value.items()}
            if result.get("@type") in ("Article", "BlogPosting", "CollectionPage", "SoftwareApplication", "WebSite"):
                result["inLanguage"] = self.language
            return result
        if isinstance(value, list):
            return [self.json_value(v, key) for v in value]
        if isinstance(value, str):
            if key in JSON_TEXT:
                return self.translate(value)
            if key in ("url", "mainEntityOfPage", "item") and value.startswith(ORIGIN + "/"):
                return ORIGIN + self.internal_url(value)
        return value

    def handle_data(self, data):
        if self.script_type == "application/ld+json":
            self.output.append(json.dumps(self.json_value(json.loads(data)), ensure_ascii=False, separators=(",", ":")).replace("<", "\\u003c"))
        elif self.script_type:
            self.output.append(data)
        else:
            self.output.append(escape(self.translate(data), quote=False))

    def render(self, source):
        self.feed(clean_generated(source))
        path = self.path
        head = ['<!-- locales:start -->',
                f'<meta property="og:locale" content="{LANGUAGES[self.language][1]}"/>']
        head.extend(f'<link rel="alternate" hreflang="{language}" href="{ORIGIN}{localized(path, language)}"/>'
                    for language in LANGUAGES)
        head.append(f'<link rel="alternate" hreflang="x-default" href="{ORIGIN}{path}"/>')
        for language, (_, locale) in LANGUAGES.items():
            if language != self.language:
                head.append(f'<meta property="og:locale:alternate" content="{locale}"/>')
        head.append('<!-- locales:end -->')
        label = self.translate("Language")
        code = {"zh-Hans": "简中", "zh-Hant": "繁中"}.get(self.language, self.language.upper())
        switcher = ['<!-- locales:start -->',
                    f'<details class="language-menu"><summary aria-label="{escape(label, quote=True)}: {LANGUAGES[self.language][0]}">{code}</summary>',
                    f'<nav aria-label="{escape(label, quote=True)}">']
        for language, (name, _) in LANGUAGES.items():
            current = ' aria-current="page"' if language == self.language else ''
            switcher.append(f'<a href="{localized(path, language)}" hreflang="{language}" lang="{language}"{current}>{name}</a>')
        switcher += ['</nav></details>', '<!-- locales:end -->']
        return "".join(self.output).replace("</head>", "\n" + "\n".join(head) + "\n</head>").replace("</header>", "\n" + "\n".join(switcher) + "\n</header>", 1)


def build(check=False):
    catalogs = {language: json.loads((ROOT / f"locales/{language}.json").read_text(encoding="utf-8"))
                for language in LANGUAGES if language != "en"}
    outputs = {}
    for file in sources():
        source = file.read_text(encoding="utf-8")
        path = page_path(file)
        for language in LANGUAGES:
            target = file if language == "en" else ROOT / language / file.relative_to(ROOT)
            outputs[target] = Renderer(path, language, catalogs.get(language)).render(source)
    paths = sorted({page_path(file) for file in sources()})
    urls = [ORIGIN + localized(path, language) for language in LANGUAGES for path in paths]
    outputs[ROOT / "sitemap.xml"] = ('<?xml version="1.0" encoding="UTF-8"?>\n'
        '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
        "".join(f"  <url><loc>{url}</loc></url>\n" for url in urls) + '</urlset>\n')
    stale = [str(file.relative_to(ROOT)) for file, content in outputs.items()
             if not file.exists() or file.read_text(encoding="utf-8") != content]
    if check and stale:
        raise ValueError("Run python scripts/localize.py; stale output: " + ", ".join(stale))
    if not check:
        for file, content in outputs.items():
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text(content, encoding="utf-8", newline="\n")
    print(f"{'Checked' if check else 'Rendered'} {len(outputs) - 1} HTML pages, {len(urls)} canonical URLs, {len(LANGUAGES)} languages.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Fail if translations or generated pages are stale")
    build(parser.parse_args().check)
