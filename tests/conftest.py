import sqlite3

import pytest

import seed


@pytest.fixture(scope="session")
def seeded_db(tmp_path_factory):
    """A freshly seeded database file, shared by tests that only read it."""
    path = tmp_path_factory.mktemp("seed") / "test.db"
    seed.build(path)
    return path


@pytest.fixture
def db(seeded_db, tmp_path):
    """A writable copy of the seeded database, for tests that break the data."""
    conn = sqlite3.connect(tmp_path / "copy.db")
    source = sqlite3.connect(seeded_db)
    source.backup(conn)
    source.close()
    conn.execute("PRAGMA foreign_keys = ON")
    yield conn
    conn.close()
