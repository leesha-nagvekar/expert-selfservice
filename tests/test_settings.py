import pytest

from settings import DEFAULT_DATABASE_PATH, ROOT, database_path


@pytest.fixture(autouse=True)
def no_env_variable(monkeypatch):
    monkeypatch.delenv("DATABASE_PATH", raising=False)


@pytest.mark.parametrize(
    "line",
    [
        'DATABASE_PATH="custom/x.db"',
        "DATABASE_PATH='custom/x.db'",
        "DATABASE_PATH=custom/x.db",
        "export DATABASE_PATH=custom/x.db",
    ],
)
def test_env_file_with_bom_and_quotes(tmp_path, line):
    env_file = tmp_path / ".env"
    env_file.write_text(f"# test settings\n{line}\n", encoding="utf-8-sig")  # with BOM
    assert database_path(env_file) == ROOT / "custom" / "x.db"


def test_bom_on_first_line(tmp_path):
    env_file = tmp_path / ".env"
    env_file.write_text("DATABASE_PATH=custom/x.db\n", encoding="utf-8-sig")
    assert database_path(env_file) == ROOT / "custom" / "x.db"


def test_environment_wins_over_env_file(tmp_path, monkeypatch):
    env_file = tmp_path / ".env"
    env_file.write_text("DATABASE_PATH=from/file.db\n", encoding="utf-8")
    monkeypatch.setenv("DATABASE_PATH", str(tmp_path / "from_env.db"))
    assert database_path(env_file) == tmp_path / "from_env.db"


def test_default_without_env_file(tmp_path):
    assert database_path(tmp_path / "missing.env") == ROOT / DEFAULT_DATABASE_PATH
