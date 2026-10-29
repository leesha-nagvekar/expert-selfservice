"""Settings shared by the scripts. Values come from the environment or .env."""

import os
from pathlib import Path

from dotenv import dotenv_values

ROOT = Path(__file__).resolve().parent
DEFAULT_DATABASE_PATH = "data/expert_register.db"


def database_path(env_file: Path = ROOT / ".env") -> Path:
    """DATABASE_PATH from the environment, then .env, then the default.

    A relative path is read from the project folder.
    """
    value = os.environ.get("DATABASE_PATH")
    if not value and env_file.exists():
        # utf-8-sig: Notepad and PowerShell may start the file with a BOM.
        value = dotenv_values(env_file, encoding="utf-8-sig").get("DATABASE_PATH")
    path = Path(value or DEFAULT_DATABASE_PATH)
    return path if path.is_absolute() else ROOT / path
