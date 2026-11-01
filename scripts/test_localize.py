"""Permanent checks of the shipped static pages and the production generator."""

import contextlib
import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urljoin, urlsplit

from localize import LANGUAGES, ORIGIN, ROOT, Renderer, build, localized, page_path, sources


class Page(HTMLParser):
    def __init__(self, file):
        super().__init__()
        self.tags, self.structured, self.ids, self.text = [], [], set(), []
        self.script_type = None
        self.in_head = False
        self.head_links = []
        self.in_header = False
        self.language_in_header = False
        self.summary = None
        self.summary_text = ""
        self.in_summary = False
        self.feed(file.read_text(encoding="utf-8"))

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        self.tags.append((tag, attrs))
        if tag == "head":
            self.in_head = True
        if tag == "header":
            self.in_header = True
        if tag == "details" and attrs.get("class") == "language-menu":
            self.language_in_header = self.in_header
        if tag == "summary":
            self.summary = attrs
            self.in_summary = True
        if self.in_head and tag == "link":
            self.head_links.append(attrs)
        if tag == "script":
            self.script_type = attrs.get("type")
        if "id" in attrs:
            self.ids.add(attrs["id"])

    def handle_endtag(self, tag):
        if tag == "script":
            self.script_type = None
        if tag == "head":
            self.in_head = False
        if tag == "header":
            self.in_header = False
        if tag == "summary":
            self.in_summary = False

    def handle_data(self, data):
        if self.in_summary:
            self.summary_text += data
        if self.script_type == "application/ld+json":
            self.structured.append(json.loads(data))
        elif data.strip():
            self.text.append(data.strip())

    def attributes(self, tag, key, value):
        return [attrs for name, attrs in self.tags if name == tag and attrs.get(key) == value]

    def alternates(self):
        return {attrs["hreflang"]: attrs["href"] for attrs in self.head_links if attrs.get("rel") == "alternate"}


class WebsiteTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pages = {"/" + file.relative_to(ROOT).as_posix().removesuffix("index.html"): Page(file)
                     for file in ROOT.rglob("index.html") if ".git" not in file.parts}

    def test_shipped_pages_match_the_production_generator(self):
        with contextlib.redirect_stdout(io.StringIO()):
            build(check=True)

    def test_blog_contains_all_migrated_articles_once_in_every_language(self):
        articles = {page_path(file) for file in sources() if file.parent.parent == ROOT / "blog"}
        self.assertTrue({
            "/blog/automate-work-setup-windows/",
            "/blog/block-apps-websites-and-launch-work-apps-windows/",
            "/blog/cold-turkey-alternative-windows/",
            "/blog/freedom-alternative-windows/",
            "/blog/schedule-apps-and-website-blocking-windows/",
        } <= articles)
        self.assertEqual(set(self.pages), {localized(page_path(file), language)
                                         for file in sources() for language in LANGUAGES})
        for language in LANGUAGES:
            with self.subTest(language=language):
                index = self.pages[localized("/blog/", language)]
                links = [attrs["href"] for tag, attrs in index.tags
                         if tag == "a" and "blog-list__item" in attrs.get("class", "").split()]
                self.assertEqual(sorted(links), sorted(localized(path, language) for path in articles))
                for path in articles:
                    page = self.pages[localized(path, language)]
                    body = next(attrs for tag, attrs in page.tags if tag == "body")
                    self.assertEqual(body["data-page-type"], "blog-post")
                    self.assertEqual(body["data-content-slug"], path.strip("/").split("/")[-1])
                    self.assertEqual([schema["@type"] for schema in page.structured
                                      if schema["@type"] in ("Article", "BlogPosting")], ["BlogPosting"])

    def test_every_language_has_every_page_and_complete_translations(self):
        strings = set()
        for file in sources():
            renderer = Renderer(page_path(file))
            renderer.render(file.read_text(encoding="utf-8"))
            strings.update(renderer.strings)
        for language in LANGUAGES:
            if language != "en":
                catalog = json.loads((ROOT / f"locales/{language}.json").read_text(encoding="utf-8"))
                self.assertEqual(strings, set(catalog), language)
                self.assertTrue(all(isinstance(value, str) and value.strip() for value in catalog.values()), language)
            for file in sources():
                path = localized(page_path(file), language)
                with self.subTest(path=path):
                    page = self.pages[path]
                    self.assertEqual([attrs["lang"] for tag, attrs in page.tags if tag == "html"], [language])
                    self.assertEqual(sum(tag == "h1" for tag, _ in page.tags), 1)
                    self.assertTrue(page.attributes("meta", "name", "description"))
                    self.assertTrue(page.attributes("meta", "name", "robots"))
                    self.assertFalse(any("noindex" in attrs.get("content", "") for tag, attrs in page.tags if tag == "meta"))
                    if language != "en":
                        self.assertNotEqual(page.text, self.pages[page_path(file)].text)
                        english_paragraphs = [text for text in self.pages[page_path(file)].text if len(text.split()) > 8]
                        self.assertFalse(set(english_paragraphs) & set(page.text), path)

    def test_canonicals_and_reciprocal_hreflang_point_to_canonical_pages(self):
        for file in sources():
            path = page_path(file)
            expected = {language: ORIGIN + localized(path, language) for language in LANGUAGES}
            expected["x-default"] = ORIGIN + path
            for language in LANGUAGES:
                with self.subTest(file=file, language=language):
                    page = self.pages[localized(page_path(file), language)]
                    self.assertEqual(page.alternates(), expected)
                    self.assertEqual(len([link for link in page.head_links if link.get("rel") == "alternate"]), len(expected))
                    self.assertEqual(page.attributes("link", "rel", "canonical"),
                                     [{"href": expected[language], "rel": "canonical"}])
                    for target in expected.values():
                        alternate = self.pages[urlsplit(target).path]
                        self.assertEqual(alternate.alternates(), expected)
                        self.assertEqual(alternate.attributes("link", "rel", "canonical")[0]["href"], target)
                    for tag, attrs in page.tags:
                        if tag == "meta" and attrs.get("property") == "og:url":
                            self.assertEqual(attrs["content"], expected[language])
                    for schema in page.structured:
                        if schema["@type"] in ("Article", "BlogPosting", "CollectionPage", "SoftwareApplication"):
                            self.assertEqual(schema["inLanguage"], language)
                        if schema["@type"] in ("Article", "BlogPosting"):
                            self.assertEqual(schema["url"], expected[language])
                            self.assertEqual(schema["mainEntityOfPage"], expected[language])

    def test_all_links_assets_fragments_and_language_switches_resolve(self):
        installer = "https://github.com/axorithlabs/axorith/releases/latest/download/axorith-setup.exe"
        for path, page in self.pages.items():
            language = path.split("/")[1] if path.split("/")[1] in LANGUAGES else "en"
            with self.subTest(path=path):
                switches = [attrs for tag, attrs in page.tags if tag == "a" and "hreflang" in attrs]
                self.assertEqual(len(switches), len(LANGUAGES))
                self.assertEqual([a["lang"] for a in switches if a.get("aria-current") == "page"], [language])
                downloads = [attrs for tag, attrs in page.tags if tag == "a" and attrs.get("href") == installer]
                self.assertTrue(downloads)
                for tag, attrs in page.tags:
                    for key in ("href", "src"):
                        if key not in attrs:
                            continue
                        url = urlsplit(urljoin(ORIGIN + path, attrs[key]))
                        if url.netloc != "axorith.com" or url.scheme != "https":
                            continue
                        target = ROOT / url.path.lstrip("/")
                        if url.path.endswith("/"):
                            target /= "index.html"
                        self.assertTrue(target.is_file(), (path, attrs[key]))
                        if url.fragment:
                            self.assertIn(url.fragment, self.pages[url.path].ids, (path, attrs[key]))
                        if tag == "a" and "hreflang" not in attrs:
                            target_language = url.path.split("/")[1]
                            self.assertEqual(target_language if target_language in LANGUAGES else "en", language)
                self.assertFalse(page.attributes("meta", "http-equiv", "refresh"))

    def test_compact_current_language_control_is_inside_the_header_on_every_page(self):
        codes = {"en": "EN", "ru": "RU", "zh-Hans": "简中", "zh-Hant": "繁中", "fr": "FR",
                 "pt": "PT", "es": "ES", "de": "DE", "ja": "JA", "nl": "NL", "pl": "PL"}
        for path, page in self.pages.items():
            language = path.split("/")[1] if path.split("/")[1] in LANGUAGES else "en"
            with self.subTest(path=path):
                self.assertTrue(page.language_in_header)
                self.assertEqual(page.summary_text, codes[language])
                self.assertIn(LANGUAGES[language][0], page.summary["aria-label"])
                self.assertFalse(page.attributes("div", "class", "language-bar"))

    def test_sitemap_contains_all_and_only_self_canonical_pages(self):
        tree = ET.parse(ROOT / "sitemap.xml")
        urls = [node.text for node in tree.findall("{*}url/{*}loc")]
        expected = {ORIGIN + path for path, page in self.pages.items()
                    if page.attributes("link", "rel", "canonical")[0]["href"] == ORIGIN + path}
        self.assertEqual(set(urls), expected)
        self.assertEqual(len(urls), len(expected))
        self.assertIn("Sitemap: " + ORIGIN + "/sitemap.xml", (ROOT / "robots.txt").read_text())
        self.assertNotIn("Disallow: /", (ROOT / "robots.txt").read_text())

    def test_missing_translation_fails_before_overwriting_pages(self):
        with tempfile.TemporaryDirectory(prefix="axorith-locales-") as directory:
            root = Path(directory)
            for name in ("scripts", "locales", "blog", "download", "privacy"):
                shutil.copytree(ROOT / name, root / name)
            shutil.copyfile(ROOT / "index.html", root / "index.html")
            catalog_path = root / "locales/ru.json"
            catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
            del catalog["Download for Windows"]
            catalog_path.write_text(json.dumps(catalog), encoding="utf-8")
            original = (root / "index.html").read_bytes()
            result = subprocess.run([sys.executable, str(root / "scripts/localize.py")],
                                    capture_output=True, timeout=30)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b"missing translation: Download for Windows", result.stderr)
            self.assertEqual((root / "index.html").read_bytes(), original)
            self.assertFalse((root / "ru/index.html").exists())


if __name__ == "__main__":
    unittest.main()
