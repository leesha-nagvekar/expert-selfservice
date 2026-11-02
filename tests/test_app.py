import re

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from settings import ROOT


@pytest.fixture
def client():
    return TestClient(create_app())


def test_home_shows_top_bar_in_danish(client):
    response = client.get("/")
    assert response.status_code == 200
    html = response.text
    assert '<html lang="da">' in html
    assert "Selvbetjening for skønsmænd" in html
    assert 'class="topbar"' in html
    assert "/static/img/vn-logo-white.png" in html
    assert "/static/img/vn-mark-white.png" in html
    for css in ("tokens.css", "fonts.css", "app.css"):
        assert f"/static/{css}" in html


def test_language_switch_sets_cookie_and_goes_back(client):
    response = client.get("/language/en", params={"next": "/"}, follow_redirects=False)
    assert response.status_code == 303
    assert response.headers["location"] == "/"
    assert client.cookies.get("lang") == "en"

    html = client.get("/").text
    assert '<html lang="en">' in html
    assert "Expert self-service" in html


@pytest.mark.parametrize(
    "next_url",
    ["//evil.example.com", "https://evil.example.com", "/\\evil.example.com", "evil", ""],
)
def test_language_switch_never_leaves_the_site(client, next_url):
    response = client.get("/language/en", params={"next": next_url}, follow_redirects=False)
    assert response.headers["location"] == "/"


def test_unknown_language_is_not_found(client):
    assert client.get("/language/de").status_code == 404


def test_unknown_language_cookie_falls_back_to_danish(client):
    client.cookies.set("lang", "de")
    assert '<html lang="da">' in client.get("/").text


def test_unknown_page_shows_plain_404(client):
    client.cookies.set("lang", "en")
    response = client.get("/nothing-here")
    assert response.status_code == 404
    assert "Page not found" in response.text
    assert 'class="topbar"' in response.text


def test_error_shows_plain_500_without_details():
    app = create_app()

    @app.get("/boom")
    def boom():
        raise RuntimeError("secret detail")

    client = TestClient(app, raise_server_exceptions=False)
    response = client.get("/boom")
    assert response.status_code == 500
    assert "Der skete en fejl" in response.text
    assert "secret detail" not in response.text
    assert "Traceback" not in response.text


COLOUR_CODE = re.compile(r"#[0-9a-fA-F]{3,8}\b|\b(?:rgb|rgba|hsl|hsla)\(")


def test_colour_codes_only_in_tokens_css():
    files = [
        *(ROOT / "static").rglob("*.css"),
        *(ROOT / "templates").rglob("*.html"),
        *(ROOT / "app").rglob("*.py"),
    ]
    offenders = [
        f"{path.relative_to(ROOT)}: {match.group()}"
        for path in files
        if path.name != "tokens.css"
        for match in COLOUR_CODE.finditer(path.read_text(encoding="utf-8"))
    ]
    assert offenders == []


def test_linked_files_are_served(client):
    html = client.get("/").text
    fonts_css = client.get("/static/fonts.css").text
    urls = re.findall(r'(?:href|src|srcset)="(/static/[^"]+)"', html)
    urls += re.findall(r'url\("?(/static/[^")]+)"?\)', fonts_css)
    assert any(url.endswith(".woff2") for url in urls)
    for url in urls:
        response = client.get(url)
        assert response.status_code == 200, url
        if url.endswith(".woff2"):
            assert response.headers["content-type"] == "font/woff2"
