import sqlite3
from pathlib import Path

import pytest

import seed
from check_data import check
from rules import (
    ACCEPTED,
    ACTIVE,
    DEMO_ANNA,
    DEMO_BO,
    DEMO_CARL,
    NO_PHONE_EXPERTS,
    STANDBY,
)


def dump(path):
    conn = sqlite3.connect(path)
    try:
        return "\n".join(conn.iterdump())
    finally:
        conn.close()


def test_same_data_every_run(seeded_db, tmp_path):
    second = tmp_path / "second.db"
    seed.build(second)
    assert dump(seeded_db) == dump(second)


def test_fresh_seed_passes_check_data(seeded_db):
    conn = sqlite3.connect(seeded_db)
    assert check(conn) == []
    conn.close()


def test_demo_postcode_8000_gives_aarhus_c(db):
    city = db.execute(
        "SELECT ci.City FROM Zipcodes z JOIN Cities ci ON ci.Id = z.CityId WHERE z.Zipcode = '8000'"
    ).fetchone()
    assert city == ("Aarhus C",)


def test_no_phone_count_matches_rule(db):
    count = db.execute(
        "SELECT COUNT(*) FROM Candidates "
        "WHERE PhoneMobile IS NULL AND PhoneWork IS NULL AND PhoneHome IS NULL"
    ).fetchone()[0]
    assert count == NO_PHONE_EXPERTS


def test_seed_gives_every_company_its_own_cvr(db):
    # The app may share a CVR between colleagues (E6), so check_data allows it.
    # The seed still draws a new one each time, so the fake data stays as it was.
    shared = db.execute(
        "SELECT cvr FROM ss_expert_company GROUP BY cvr HAVING COUNT(*) > 1"
    ).fetchall()
    assert shared == []


def test_seed_cvrs_never_start_with_0(db):
    starts = db.execute("SELECT DISTINCT substr(cvr, 1, 1) FROM ss_expert_company").fetchall()
    assert ("0",) not in starts


def test_standby_ends_after_demo(db):
    (earliest,) = db.execute(
        "SELECT MIN(UntilDate) FROM Candidates WHERE Status = ?", (STANDBY,)
    ).fetchone()
    assert earliest > seed.DEMO_DATE.isoformat()


@pytest.mark.parametrize(
    "statement", ["UPDATE ss_change_log SET field = 'x'", "DELETE FROM ss_change_log"]
)
def test_change_log_is_append_only(db, statement):
    db.execute(
        "INSERT INTO ss_change_log (candidate_id, actor_type, actor_id, field, created_at) "
        "VALUES (1, 'expert', 1, 'Street', '2026-10-01T10:00:00Z')"
    )
    with pytest.raises(sqlite3.IntegrityError, match="append-only"):
        db.execute(statement)


def test_seed_without_reset_refuses_existing_file(tmp_path, monkeypatch):
    path = tmp_path / "existing.db"
    path.write_bytes(b"keep me")
    monkeypatch.setenv("DATABASE_PATH", str(path))
    assert seed.main([]) == 1
    assert path.read_bytes() == b"keep me"


def test_seed_with_reset_rebuilds(tmp_path, monkeypatch):
    path = tmp_path / "existing.db"
    path.write_bytes(b"old")
    monkeypatch.setenv("DATABASE_PATH", str(path))
    assert seed.main(["--reset"]) == 0
    conn = sqlite3.connect(path)
    assert conn.execute("SELECT COUNT(*) FROM Candidates").fetchone()[0] == 100
    conn.close()


def fail_halfway(self):
    raise RuntimeError("build failed halfway")


def test_failed_build_keeps_old_database(tmp_path, monkeypatch):
    path = tmp_path / "existing.db"
    path.write_bytes(b"old")
    monkeypatch.setattr(seed.Generator, "add_random_experts", fail_halfway)
    with pytest.raises(RuntimeError):
        seed.build(path)
    assert path.read_bytes() == b"old"
    assert not seed.building_path(path).exists()


def test_failed_build_leaves_no_file(tmp_path, monkeypatch):
    path = tmp_path / "new.db"
    monkeypatch.setattr(seed.Generator, "add_random_experts", fail_halfway)
    with pytest.raises(RuntimeError):
        seed.build(path)
    assert list(tmp_path.iterdir()) == []


def test_reset_on_locked_file_keeps_old_database(tmp_path, monkeypatch, capsys):
    path = tmp_path / "existing.db"
    path.write_bytes(b"old")
    monkeypatch.setenv("DATABASE_PATH", str(path))

    def locked(source, target):
        raise PermissionError("file is in use")

    monkeypatch.setattr(seed.os, "replace", locked)
    assert seed.main(["--reset"]) == 1
    assert "old database is unchanged" in capsys.readouterr().out
    assert path.read_bytes() == b"old"
    assert not seed.building_path(path).exists()


def test_build_removes_leftover_temp_file(tmp_path):
    path = tmp_path / "new.db"
    seed.building_path(path).write_bytes(b"left over from a killed build")
    seed.build(path)
    assert not seed.building_path(path).exists()
    assert path.exists()


def test_build_removes_leftover_side_files(tmp_path):
    path = tmp_path / "new.db"
    temp = seed.building_path(path)
    side_files = [Path(f"{temp}{suffix}") for suffix in ("-journal", "-wal", "-shm")]
    for side_file in side_files:
        side_file.write_bytes(b"left over from a killed build")
    seed.build(path)
    assert [p for p in side_files if p.exists()] == []
    assert path.exists()


def test_build_removes_old_side_files(tmp_path):
    path = tmp_path / "existing.db"
    path.write_bytes(b"old")
    side_files = [Path(f"{path}{suffix}") for suffix in ("-journal", "-wal", "-shm")]
    for side_file in side_files:
        side_file.write_bytes(b"belongs to the old database")
    seed.build(path)
    assert [p for p in side_files if p.exists()] == []


def test_failed_check_keeps_old_database(tmp_path, monkeypatch, capsys):
    path = tmp_path / "existing.db"
    path.write_bytes(b"old")
    monkeypatch.setattr(seed, "check", lambda conn: ["Expert 4 has a made-up problem"])

    assert seed.build(path) == ["Expert 4 has a made-up problem"]
    assert path.read_bytes() == b"old"
    assert not seed.building_path(path).exists()

    monkeypatch.setenv("DATABASE_PATH", str(path))
    assert seed.main(["--reset"]) == 1
    assert "The old database is unchanged" in capsys.readouterr().out
    assert path.read_bytes() == b"old"


def test_temp_file_always_ends_in_db():
    assert seed.building_path(Path("x/register.sqlite3")) == Path("x/register.building.db")


def test_schema_file_turns_on_foreign_keys():
    conn = sqlite3.connect(":memory:")
    conn.executescript(seed.SCHEMA_FILE.read_text(encoding="utf-8"))
    with pytest.raises(sqlite3.IntegrityError, match="FOREIGN KEY"):
        conn.execute("INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (999, 'EL')")
    conn.close()


def test_demo_experts_match_spec(db):
    # The demo script relies on these facts (docs/SPEC.md section 7).
    def expert(email):
        return db.execute(
            "SELECT c.Id, c.Status, z.Zipcode, c.PhoneMobile, c.PhoneWork, c.PhoneHome, "
            "c.Company, "
            "(SELECT COUNT(*) FROM ss_expert_company e WHERE e.candidate_id = c.Id), "
            "(SELECT COUNT(*) FROM CandidateSkills s WHERE s.CandidateId = c.Id), "
            "(SELECT COUNT(*) FROM CandidateTags t WHERE t.CandidateId = c.Id) "
            "FROM Candidates c LEFT JOIN Zipcodes z ON z.Id = c.ZipcodeId WHERE c.Email = ?",
            (email,),
        ).fetchone()

    anna_id, anna_status, anna_postcode, *_ = expert(DEMO_ANNA)
    assert (anna_id, anna_status, anna_postcode) == (1, ACTIVE, "2100")

    bo_id, bo_status, _, mobile, work, home, company, cvr_rows, *_ = expert(DEMO_BO)
    assert (bo_id, bo_status) == (2, STANDBY)
    assert (mobile, work, home, company, cvr_rows) == (None, None, None, None, 0)

    carl_id, carl_status, *_, skills, roles = expert(DEMO_CARL)
    assert (carl_id, carl_status, skills, roles) == (3, ACCEPTED, 4, 2)


def test_candidate_files_matches_vba_schema(db):
    # (name, NOT NULL, default) per column, in the order of VBA's CandidateFiles.
    columns = [
        (name, notnull, default)
        for _, name, _, notnull, default, _ in db.execute("PRAGMA table_info(CandidateFiles)")
    ]
    assert columns == [
        ("Id", 0, None),
        ("CandidateId", 1, None),
        ("FileContent", 0, None),
        ("FileName", 1, None),
        ("CreationDateTime", 1, "'1900-01-01 00:00:00'"),
        ("IsPrimary", 1, "0"),
    ]
    # (table, from, to) per foreign key.
    keys = [
        (table, from_column, to_column)
        for _, _, table, from_column, to_column, *_ in db.execute(
            "PRAGMA foreign_key_list(CandidateFiles)"
        )
    ]
    assert keys == [("Candidates", "CandidateId", "Id")]


def test_candidate_files_rejects_unknown_expert(db):
    with pytest.raises(sqlite3.IntegrityError, match="FOREIGN KEY"):
        db.execute("INSERT INTO CandidateFiles (CandidateId, FileName) VALUES (999, 'cv.pdf')")


def test_staff_email_is_unique_in_any_case(db):
    with pytest.raises(sqlite3.IntegrityError, match="UNIQUE"):
        db.execute(
            "INSERT INTO ss_staff_users (name, email, created_at) "
            "VALUES ('Kopi', 'SOFIE.STAFF@example.com', '2026-10-01T08:00:00Z')"
        )


def test_lockout_is_one_row_per_email_in_any_case(db):
    # Otherwise typing the email in capitals would start a new count of failed tries.
    insert = "INSERT INTO ss_login_lockouts (email, failed_count) VALUES (?, ?)"
    db.execute(insert, ("anna.demo@example.com", 5))
    with pytest.raises(sqlite3.IntegrityError, match="UNIQUE"):
        db.execute(insert, ("ANNA.DEMO@example.com", 0))


@pytest.mark.parametrize(
    ("name", "expected"),
    [("Søren", "soeren"), ("Æbelø", "aebeloe"), ("Åse", "aase"), ("Anne Marie", "anne-marie")],
)
def test_email_name_part(name, expected):
    assert seed._ascii(name) == expected
