"""Check the test database. Exit code 0 = OK, 1 = problems.

Run: python check_data.py [path-to-db]
"""

import re
import sqlite3
import sys
from collections import Counter
from pathlib import Path

from rules import (
    DANISH_POSTCODE_SQL,
    DEMO_EMAILS,
    EMAIL_DOMAIN,
    EXPECTED_EXPERTS,
    EXPECTED_STAFF,
    MAX_LENGTHS,
    MIN_REGISTRATION_ID,
    NO_PHONE_EXPERTS,
    RETIRED,
    ROLES_PER_EXPERT,
    SKILLS_PER_EXPERT,
    STANDBY,
    STATUS_NAMES,
    cvr_is_valid,
    is_valid_email,
)
from settings import database_path


def check(conn: sqlite3.Connection) -> list[str]:
    """Return a list of problems. An empty list means the data is OK."""
    problems = []
    problems += _check_counts(conn)
    problems += _check_emails(conn)
    problems += _check_keys(conn)
    problems += _check_experts(conn)
    problems += _check_company(conn)
    return problems


def _check_counts(conn):
    problems = []
    experts = conn.execute("SELECT COUNT(*) FROM Candidates").fetchone()[0]
    if experts != EXPECTED_EXPERTS:
        problems.append(f"Expected {EXPECTED_EXPERTS} experts, found {experts}")

    staff = conn.execute("SELECT COUNT(*) FROM ss_staff_users").fetchone()[0]
    if staff != EXPECTED_STAFF:
        problems.append(f"Expected {EXPECTED_STAFF} staff users, found {staff}")

    for email in DEMO_EMAILS:
        row = conn.execute(
            "SELECT Status FROM Candidates WHERE lower(Email) = lower(?)", (email,)
        ).fetchone()
        if row is None:
            problems.append(f"Demo expert {email} is missing")
        elif row[0] == RETIRED:
            problems.append(f"Demo expert {email} is Retired and can't log in")
    return problems


def _check_emails(conn):
    problems = []
    rows = conn.execute(
        "SELECT 'expert ' || Id, Email FROM Candidates "
        "UNION ALL SELECT 'staff ' || id, email FROM ss_staff_users"
    ).fetchall()
    for who, email in rows:
        if not email:
            problems.append(f"{who} has no email")
            continue
        if not email.lower().endswith(EMAIL_DOMAIN):
            problems.append(f"{who} email {email} doesn't end in {EMAIL_DOMAIN}")
        if not is_valid_email(email):
            problems.append(f"{who} email {email!r} is not a valid email")

    counts = Counter(email.lower() for _, email in rows if email)
    for email, count in sorted(counts.items()):
        if count > 1:
            problems.append(f"Email {email} is used {count} times")
    return problems


def _check_keys(conn):
    problems = []
    for table, rowid, parent, _ in conn.execute("PRAGMA foreign_key_check"):
        problems.append(f"{table} row {rowid} points to a missing {parent} row")

    for table in ("Fags", "ProfessionTags"):
        dupes = conn.execute(f"SELECT Id FROM {table} GROUP BY Id HAVING COUNT(*) > 1").fetchall()
        for (row_id,) in dupes:
            problems.append(f"{table}.Id {row_id} is used more than once")

    unknown_skills = conn.execute(
        "SELECT CandidateId, Skill FROM CandidateSkills "
        "WHERE Skill IS NULL OR Skill NOT IN (SELECT Fag FROM Fags)"
    ).fetchall()
    for candidate_id, skill in unknown_skills:
        problems.append(f"Expert {candidate_id} has skill {skill!r}, not in Fags")

    bad_zip = conn.execute(
        "SELECT c.Id, z.Zipcode FROM Candidates c "
        "LEFT JOIN Zipcodes z ON z.Id = c.ZipcodeId "
        "LEFT JOIN Cities ci ON ci.Id = z.CityId "
        f"WHERE z.Id IS NULL OR ci.Id IS NULL OR NOT ({DANISH_POSTCODE_SQL})"
    ).fetchall()
    for candidate_id, zipcode in bad_zip:
        problems.append(f"Expert {candidate_id} has no valid Danish postcode ({zipcode!r})")
    return problems


def _check_experts(conn):
    problems = []
    problems += _check_lengths(conn)
    rows = conn.execute(
        "SELECT Id, FirstName, LastName, RegistrationId, "
        "Status, UntilDate, OldStatus, PhoneMobile, PhoneWork, PhoneHome "
        "FROM Candidates"
    ).fetchall()

    registration_ids = Counter()
    no_phone = 0
    for (
        expert_id, first, last, registration_id,
        status, until_date, old_status, mobile, work, home,
    ) in rows:  # fmt: skip
        who = f"Expert {expert_id}"
        if not first or not last:
            problems.append(f"{who} is missing a first or last name")

        if (
            not re.fullmatch(r"\d+", registration_id or "")
            or int(registration_id) < MIN_REGISTRATION_ID
        ):
            problems.append(
                f"{who} RegistrationId {registration_id!r} is not a test number "
                f"(digits, {MIN_REGISTRATION_ID} or more)"
            )
        if registration_id is not None:  # a missing one is reported just above
            registration_ids[registration_id] += 1

        if status not in STATUS_NAMES:
            problems.append(f"{who} has unknown Status {status}")
        if status == STANDBY and (until_date is None or old_status is None):
            problems.append(f"{who} is Standby without UntilDate and OldStatus")

        if not (mobile or work or home):
            no_phone += 1

    for registration_id, count in sorted(registration_ids.items()):
        if count > 1:
            problems.append(f"RegistrationId {registration_id} is used {count} times")

    if no_phone != NO_PHONE_EXPERTS:
        problems.append(f"{no_phone} experts have no phone, expected {NO_PHONE_EXPERTS}")

    # COUNT(s.Skill) leaves out NULLs, so an empty skill (reported as "not in
    # Fags") isn't also reported as a duplicate.
    problems += _check_per_expert(
        conn,
        "skills",
        "SELECT c.Id, COUNT(s.Id), COUNT(s.Skill), COUNT(DISTINCT s.Skill) FROM Candidates c "
        "LEFT JOIN CandidateSkills s ON s.CandidateId = c.Id GROUP BY c.Id",
        SKILLS_PER_EXPERT,
    )
    problems += _check_per_expert(
        conn,
        "roles",
        "SELECT c.Id, COUNT(t.Id), COUNT(t.TagId), COUNT(DISTINCT t.TagId) FROM Candidates c "
        "LEFT JOIN CandidateTags t ON t.CandidateId = c.Id GROUP BY c.Id",
        ROLES_PER_EXPERT,
    )

    two_languages = conn.execute(
        "SELECT COUNT(*) FROM (SELECT candidate_id FROM ss_expert_languages "
        "GROUP BY candidate_id HAVING COUNT(*) >= 2)"
    ).fetchone()[0]
    if two_languages == 0:
        problems.append("No expert has two languages")
    return problems


def _check_per_expert(conn, what, sql, allowed):
    """sql gives per expert: Id, rows, filled-in values, different values."""
    low, high = allowed
    problems = []
    for expert_id, rows, filled, distinct in conn.execute(sql):
        if not low <= rows <= high:
            problems.append(f"Expert {expert_id} has {rows} {what}, expected {low}-{high}")
        if filled != distinct:
            problems.append(f"Expert {expert_id} has one of their {what} twice")
    return problems


def _check_lengths(conn):
    """Every text column in MAX_LENGTHS fits its limit from schema.sql.

    Staff emails get the same limit, because a staff user logs in like an expert.
    """
    checks = [
        ("Expert", "Candidates", "Id", column, limit) for column, limit in MAX_LENGTHS.items()
    ]
    checks.append(("Staff user", "ss_staff_users", "id", "email", MAX_LENGTHS["Email"]))

    problems = []
    for who, table, id_column, column, limit in checks:
        too_long = conn.execute(
            f'SELECT {id_column}, LENGTH("{column}") FROM {table} WHERE LENGTH("{column}") > ?',
            (limit,),
        ).fetchall()
        for row_id, length in too_long:
            problems.append(f"{who} {row_id} {column} is {length} characters, max is {limit}")
    return problems


def _check_company(conn):
    problems = []
    rows = conn.execute(
        "SELECT candidate_id, cvr FROM ss_expert_company WHERE cvr IS NOT NULL AND cvr <> ''"
    ).fetchall()
    for candidate_id, cvr in rows:
        if not cvr_is_valid(cvr):
            problems.append(f"Expert {candidate_id} has an invalid CVR {cvr}")
    counts = Counter(cvr for _, cvr in rows)
    for cvr, count in sorted(counts.items()):
        if count > 1:
            problems.append(f"CVR {cvr} is used {count} times")
    return problems


def main(argv: list[str]) -> int:
    # A path given on the command line is read from the current folder.
    path = Path(argv[0]).resolve() if argv else database_path()
    if not path.exists():
        print(f"No database at {path}. Run: python seed.py --reset")
        return 1
    conn = sqlite3.connect(f"{path.as_uri()}?mode=ro", uri=True)
    try:
        problems = check(conn)
    finally:
        conn.close()
    for problem in problems:
        print(problem)
    print(f"check_data: {'OK' if not problems else f'{len(problems)} problem(s)'}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
