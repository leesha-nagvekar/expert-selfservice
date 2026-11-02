"""The web app: static files, templates, the language switch and error pages."""

import logging
import mimetypes

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import RedirectResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.i18n import COOKIE_NAME, LANGUAGES, language_from, translator
from settings import ROOT

# Windows doesn't always know this type, and would serve the fonts as plain bytes.
mimetypes.add_type("font/woff2", ".woff2")

logger = logging.getLogger(__name__)
templates = Jinja2Templates(directory=ROOT / "templates")

ONE_YEAR = 365 * 24 * 60 * 60


def render(request: Request, template: str, status_code: int = 200, **context):
    """Render a template with the visitor's language and t() always available."""
    lang = language_from(request)
    return templates.TemplateResponse(
        request,
        template,
        {"lang": lang, "languages": LANGUAGES, "t": translator(lang), **context},
        status_code=status_code,
    )


def safe_next(url: str) -> str:
    """Only a path on this site. Anything else could send people to another site."""
    if url.startswith("/") and not url.startswith(("//", "/\\")):
        return url
    return "/"


def create_app() -> FastAPI:
    app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)
    app.mount("/static", StaticFiles(directory=ROOT / "static"), name="static")

    @app.get("/")
    def home(request: Request):
        return render(request, "home.html")

    @app.get("/language/{code}")
    def set_language(code: str, next: str = "/"):
        if code not in LANGUAGES:
            raise HTTPException(status_code=404)
        response = RedirectResponse(safe_next(next), status_code=303)
        response.set_cookie(COOKIE_NAME, code, max_age=ONE_YEAR, httponly=True, samesite="lax")
        return response

    @app.exception_handler(StarletteHTTPException)
    def http_error(request: Request, exc: StarletteHTTPException):
        # Every HTTP error (404, 405, ...) shows the same plain "Page not found".
        return render(request, "error.html", exc.status_code, kind="not_found")

    @app.exception_handler(Exception)
    def server_error(request: Request, exc: Exception):
        logger.exception("Unexpected error on %s", request.url.path)
        return render(request, "error.html", 500, kind="server")

    return app


app = create_app()
