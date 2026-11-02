"""All text the app shows comes from i18n/<language>.json."""

import json

from fastapi import Request

from settings import ROOT

LANGUAGES = ("da", "en")
DEFAULT_LANGUAGE = "da"
COOKIE_NAME = "lang"

TEXTS = {
    lang: json.loads((ROOT / "i18n" / f"{lang}.json").read_text(encoding="utf-8"))
    for lang in LANGUAGES
}


def language_from(request: Request) -> str:
    """The language in the cookie, or Danish if it is missing or unknown."""
    lang = request.cookies.get(COOKIE_NAME)
    return lang if lang in LANGUAGES else DEFAULT_LANGUAGE


def translator(lang: str):
    """t(key, **values) for one language. A missing key raises KeyError."""
    texts = TEXTS[lang]

    def t(key: str, **values) -> str:
        text = texts[key]
        return text.format(**values) if values else text

    return t
