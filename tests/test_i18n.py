import json
import re

import pytest

from app import i18n
from settings import ROOT


def load(lang):
    return json.loads((ROOT / "i18n" / f"{lang}.json").read_text(encoding="utf-8"))


def test_all_languages_have_the_same_keys():
    keys = {lang: set(load(lang)) for lang in i18n.LANGUAGES}
    first = keys[i18n.LANGUAGES[0]]
    for lang, lang_keys in keys.items():
        assert lang_keys == first, f"{lang}: {lang_keys ^ first}"


@pytest.mark.parametrize("lang", i18n.LANGUAGES)
def test_no_empty_texts(lang):
    assert [key for key, text in load(lang).items() if not text.strip()] == []


def test_every_key_used_in_templates_exists():
    # Plain keys only, e.g. t("home.title"). Keys built in the template are listed below.
    used = set()
    for path in (ROOT / "templates").rglob("*.html"):
        text = path.read_text(encoding="utf-8")
        used |= set(re.findall(r"""\bt\(\s*["']([^"']+)["']\s*[,)]""", text))
    assert used, "no t() calls found in templates"
    for lang in i18n.LANGUAGES:
        assert used - set(load(lang)) == set(), lang


def test_keys_built_in_templates_exist():
    built = {f"language.{code}" for code in i18n.LANGUAGES}  # base.html
    built |= {
        f"error.{kind}.{part}" for kind in ("not_found", "server") for part in ("title", "text")
    }  # error.html
    for lang in i18n.LANGUAGES:
        assert built - set(load(lang)) == set(), lang


def test_missing_key_raises():
    with pytest.raises(KeyError):
        i18n.translator("da")("no.such.key")


def test_placeholders_are_filled_in(monkeypatch):
    monkeypatch.setitem(i18n.TEXTS["en"], "test.minutes", "Try again in {minutes} minutes.")
    assert i18n.translator("en")("test.minutes", minutes=15) == "Try again in 15 minutes."
