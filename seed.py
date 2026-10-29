"""Build the SQLite test database with 100 fake experts.

Run: python seed.py --reset

Every run gives the same 100 experts: Faker and random use a fixed seed, and
dates are counted from a fixed reference date instead of today. All data is
made up. All emails end in @example.com.
"""

import argparse
import csv
import os
import random
import sqlite3
import sys
import unicodedata
from datetime import date, timedelta
from pathlib import Path

from faker import Faker

from check_data import check
from rules import (
    ACCEPTED,
    ACTIVE,
    DANISH_POSTCODE_SQL,
    DEMO_ANNA,
    DEMO_BO,
    DEMO_CARL,
    DEMO_EMAILS,
    EVALUATED,
    MIN_REGISTRATION_ID,
    NO_PHONE_EXPERTS,
    OBSERVATION,
    RETIRED,
    ROLES_PER_EXPERT,
    SKILLS_PER_EXPERT,
    STANDBY,
    STATUS_NAMES,
    cvr_is_valid,
)
from settings import ROOT, database_path

SEED = 2026
REFERENCE_DATE = date(2026, 10, 1)
# The hand-over demo. Standby periods end after it, so no Standby expert
# shows an end date in the past during the demo.
DEMO_DATE = date(2026, 11, 27)
FIRST_CREATED = date(2005, 1, 1)
STAFF_CREATED_AT = "2026-10-01T08:00:00Z"

SCHEMA_FILE = ROOT / "db" / "schema_sqlite.sql"
LOOKUPS = ROOT / "data" / "lookups"
LOOKUP_TABLES = ("Regions", "Cities", "Zipcodes", "Fags", "ProfessionTags")

RANDOM_EXPERTS = 97

# Status -> how many of the random experts get it.
STATUS_COUNTS = {
    ACTIVE: 52,
    ACCEPTED: 10,
    OBSERVATION: 8,
    EVALUATED: 10,
    STANDBY: 10,
    RETIRED: 7,
}

DESIGNATIONS = (
    "Arkitekt",
    "Byggeleder",
    "Bygningskonstruktør",
    "Elinstallatør",
    "Ingeniør",
    "Landinspektør",
    "Murermester",
    "Rådgivende ingeniør",
    "Tømrermester",
    "VVS-installatør",
)
LANGUAGES = ("Dansk", "Engelsk", "Tysk", "Svensk", "Norsk", "Fransk")
MAIN_ROLE = "Skønsmand"

STAFF_USERS = (
    ("Sofie Madsen", "sofie.staff@example.com"),
    ("Mads Kjær", "mads.staff@example.com"),
)

DEMO_EXPERTS = (
    {
        "FirstName": "Anna",
        "LastName": "Holm",
        "Email": DEMO_ANNA,
        "Status": ACTIVE,
        "Street": "Østerbrogade 64",
        "postcode": "2100",
        "PhoneMobile": "+45 20 11 22 33",
        "PhoneWork": "33 44 55 66",
        "Company": "Holm Rådgivning ApS",
        "has_cvr": True,
        "Designation": "Rådgivende ingeniør",
        "Created": "2015-03-02",
        "LastUpdated": "2025-11-14",
        "skills": ("EL", "Maskinanlæg", "Varmepumpe"),
        "roles": ("Skønsmand",),
        "languages": ("Dansk", "Engelsk"),
    },
    {
        "FirstName": "Bo",
        "LastName": "Lund",
        "Email": DEMO_BO,
        "Status": STANDBY,
        "OldStatus": ACTIVE,
        "UntilDate": "2027-03-31",
        "Street": "Vestergade 8",
        "postcode": "5000",
        "Created": "2019-06-17",
        "LastUpdated": "2026-08-03",
        "skills": ("Veje & belægning",),
        "roles": ("Sagkyndig",),
        "languages": ("Dansk",),
    },
    {
        "FirstName": "Carl",
        "LastName": "Berg",
        "Email": DEMO_CARL,
        "Status": ACCEPTED,
        "Street": "Boulevarden 21",
        "postcode": "9000",
        "PhoneMobile": "+45 40 50 60 70",
        "PhoneWork": "98 12 34 56",
        "Company": "Berg & Co. A/S",
        "has_cvr": True,
        "Designation": "Byggeleder",
        "Created": "2021-01-11",
        "LastUpdated": "2026-02-20",
        "skills": ("Bygherrerådgivning", "Fiber", "Netværk", "Sikring"),
        "roles": ("Skønsmand", "Mediator"),
        "languages": ("Dansk", "Tysk"),
    },
)

PHONE_COLUMNS = ("PhoneMobile", "PhoneWork", "PhoneHome")

# Random experts with no phone at all, so that the total (with bo.demo) is NO_PHONE_EXPERTS.
RANDOM_EXPERTS_WITHOUT_PHONE = NO_PHONE_EXPERTS - sum(
    not any(demo.get(column) for column in PHONE_COLUMNS) for demo in DEMO_EXPERTS
)

CANDIDATE_COLUMNS = (
    "FirstName",
    "MiddleName",
    "LastName",
    "Email",
    "RegistrationId",
    "Status",
    "OldStatus",
    "UntilDate",
    "Street",
    "ZipcodeId",
    "PhoneMobile",
    "PhoneWork",
    "PhoneHome",
    "Company",
    "Designation",
    "Bygherre",
    "Rådgiver",
    "Entreprenør",
    "Created",
    "LastUpdated",
)


class Generator:
    """Makes the fake data. All randomness goes through self.rng and self.fake."""

    def __init__(self, conn: sqlite3.Connection):
        self.conn = conn
        self.rng = random.Random(SEED)
        self.fake = Faker("da_DK")
        self.fake.seed_instance(SEED)
        self.used_emails: set[str] = set()
        self.used_cvrs: set[str] = set()
        self.registration_ids = iter(
            self.rng.sample(range(MIN_REGISTRATION_ID, 1000000), RANDOM_EXPERTS + len(DEMO_EXPERTS))
        )

        self.danish_zipcodes = dict(
            conn.execute(
                "SELECT z.Zipcode, z.Id FROM Zipcodes z JOIN Cities ci ON ci.Id = z.CityId "
                f"WHERE {DANISH_POSTCODE_SQL} ORDER BY z.Zipcode"
            ).fetchall()
        )
        self.fags = [r[0] for r in conn.execute("SELECT Fag FROM Fags ORDER BY Id")]
        self.tag_ids = dict(conn.execute("SELECT Tag, Id FROM ProfessionTags ORDER BY Id"))
        self.language_ids = {name: i for i, name in enumerate(LANGUAGES, start=1)}

    # -- helpers -----------------------------------------------------------

    def email_for(self, first: str, last: str) -> str:
        base = f"{_ascii(first)}.{_ascii(last)}"
        email, number = f"{base}@example.com", 2
        while email in self.used_emails:
            email, number = f"{base}{number}@example.com", number + 1
        self.used_emails.add(email)
        return email

    def cvr(self) -> str:
        """A new CVR number that passes rules.cvr_is_valid."""
        while True:
            first7 = str(self.rng.randint(1, 9)) + "".join(
                str(self.rng.randint(0, 9)) for _ in range(6)
            )
            # Exactly one check digit 0-10 passes. If it's 10, draw again.
            cvr = next(
                (first7 + d for d in "0123456789" if cvr_is_valid(first7 + d)),
                None,
            )
            if cvr and cvr not in self.used_cvrs:
                self.used_cvrs.add(cvr)
                return cvr

    def phone(self) -> str:
        digits = str(self.rng.randint(2, 9)) + "".join(
            str(self.rng.randint(0, 9)) for _ in range(7)
        )
        if self.rng.random() < 0.5:
            return "+45 " + " ".join(digits[i : i + 2] for i in range(0, 8, 2))
        return digits

    def date_between(self, start: date, end: date) -> date:
        return start + timedelta(days=self.rng.randint(0, (end - start).days))

    # -- inserts -----------------------------------------------------------

    def insert_expert(self, values: dict, skills, roles, languages, cvr) -> int:
        columns = ", ".join(f'"{c}"' for c in CANDIDATE_COLUMNS)
        placeholders = ", ".join("?" for _ in CANDIDATE_COLUMNS)
        cursor = self.conn.execute(
            f"INSERT INTO Candidates ({columns}) VALUES ({placeholders})",
            [values.get(c) for c in CANDIDATE_COLUMNS],
        )
        expert_id = cursor.lastrowid
        self.conn.executemany(
            "INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (?, ?)",
            [(expert_id, skill) for skill in skills],
        )
        self.conn.executemany(
            "INSERT INTO CandidateTags (CandidateId, TagId) VALUES (?, ?)",
            [(expert_id, self.tag_ids[role]) for role in roles],
        )
        self.conn.executemany(
            "INSERT INTO ss_expert_languages (candidate_id, language_id) VALUES (?, ?)",
            [(expert_id, self.language_ids[lang]) for lang in languages],
        )
        if cvr:
            self.conn.execute(
                "INSERT INTO ss_expert_company (candidate_id, cvr) VALUES (?, ?)",
                (expert_id, cvr),
            )
        return expert_id

    def add_languages_and_staff(self) -> None:
        self.conn.executemany(
            "INSERT INTO ss_languages (id, name) VALUES (?, ?)",
            [(i, name) for name, i in self.language_ids.items()],
        )
        self.conn.executemany(
            "INSERT INTO ss_staff_users (name, email, created_at) VALUES (?, ?, ?)",
            [(name, email, STAFF_CREATED_AT) for name, email in STAFF_USERS],
        )

    def add_demo_experts(self) -> None:
        for demo in DEMO_EXPERTS:
            self.used_emails.add(demo["Email"])
            values = {
                **demo,
                "RegistrationId": str(next(self.registration_ids)),
                "ZipcodeId": self.danish_zipcodes[demo["postcode"]],
                "Bygherre": 0,
                "Rådgiver": 1,
                "Entreprenør": 0,
            }
            cvr = self.cvr() if demo.get("has_cvr") else None
            self.insert_expert(values, demo["skills"], demo["roles"], demo["languages"], cvr)

    def add_random_experts(self) -> None:
        statuses = [s for s, n in STATUS_COUNTS.items() for _ in range(n)]
        self.rng.shuffle(statuses)
        without_phone = set(self.rng.sample(range(RANDOM_EXPERTS), RANDOM_EXPERTS_WITHOUT_PHONE))
        zip_ids = list(self.danish_zipcodes.values())
        other_roles = [t for t in self.tag_ids if t != MAIN_ROLE]
        other_languages = [lang for lang in LANGUAGES if lang != "Dansk"]

        for index in range(RANDOM_EXPERTS):
            first = self.fake.first_name()
            last = self.fake.last_name()
            middle = self.fake.last_name() if self.rng.random() < 0.15 else None
            status = statuses[index]
            created = self.date_between(FIRST_CREATED, REFERENCE_DATE)

            values = {
                "FirstName": first,
                "MiddleName": middle,
                "LastName": last,
                "Email": self.email_for(first, last),
                "RegistrationId": str(next(self.registration_ids)),
                "Status": status,
                "Street": f"{self.fake.street_name()} {self.fake.building_number()}",
                "ZipcodeId": self.rng.choice(zip_ids),
                "Designation": self.rng.choice(DESIGNATIONS),
                "Bygherre": int(self.rng.random() < 0.4),
                "Rådgiver": int(self.rng.random() < 0.4),
                "Entreprenør": int(self.rng.random() < 0.4),
                "Created": created.isoformat(),
                "LastUpdated": self.date_between(created, REFERENCE_DATE).isoformat(),
            }
            if status == STANDBY:
                values["OldStatus"] = self.rng.randint(ACTIVE, EVALUATED)
                values["UntilDate"] = (
                    DEMO_DATE + timedelta(days=self.rng.randint(30, 365))
                ).isoformat()
            if index not in without_phone:
                values["PhoneMobile"] = self.phone()
                if self.rng.random() < 0.5:
                    values["PhoneWork"] = self.phone()
                if self.rng.random() < 0.3:
                    values["PhoneHome"] = self.phone()

            cvr = None
            if self.rng.random() < 0.8:
                values["Company"] = self.fake.company()
                if self.rng.random() < 0.85:
                    cvr = self.cvr()

            skills = self.rng.sample(self.fags, self.rng.randint(*SKILLS_PER_EXPERT))
            roles = [MAIN_ROLE] if self.rng.random() < 0.8 else []
            roles += self.rng.sample(other_roles, self.rng.randint(*ROLES_PER_EXPERT) - len(roles))
            languages = ["Dansk"]
            if self.rng.random() < 0.3:
                languages.append(self.rng.choice(other_languages))

            self.insert_expert(values, skills, roles, languages, cvr)


def _ascii(name: str) -> str:
    """Name part for an email: lowercase, æ/ø/å spelled out, no accents."""
    name = name.lower().replace("æ", "ae").replace("ø", "oe").replace("å", "aa")
    name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    name = "-".join(name.split())
    return "".join(ch for ch in name if ch.isalnum() or ch == "-")


def load_lookups(conn: sqlite3.Connection) -> None:
    for table in LOOKUP_TABLES:
        with (LOOKUPS / f"{table}.csv").open(encoding="utf-8-sig", newline="") as f:
            reader = csv.reader(f)
            header = next(reader)
            rows = [[value if value != "" else None for value in row] for row in reader]
        columns = ", ".join(header)
        placeholders = ", ".join("?" for _ in header)
        conn.executemany(f"INSERT INTO {table} ({columns}) VALUES ({placeholders})", rows)


def building_path(path: Path) -> Path:
    """The temp file a build writes to. It always ends in .db, so Git ignores it."""
    return path.with_name(f"{path.stem}.building.db")


def _side_files(path: Path) -> list[Path]:
    """The files SQLite may keep next to a database (rollback journal, WAL)."""
    return [Path(f"{path}{suffix}") for suffix in ("-journal", "-wal", "-shm")]


def _remove_temp(temp: Path) -> None:
    """Delete the temp file and its side files.

    A journal left by a killed build would otherwise be replayed into the next
    build's new file.
    """
    for file in (temp, *_side_files(temp)):
        file.unlink(missing_ok=True)


def build(path: Path) -> list[str]:
    """Build a new database and put it at path, replacing any old one.

    The build writes to a temp file and runs check_data on it first. The old
    database is only replaced when the build succeeded and passed check_data,
    so a failed build never leaves a half-built or broken file.

    Returns the check_data problems. If there are any, nothing was replaced.
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = building_path(path)
    _remove_temp(temp)  # left over from a build that was killed
    try:
        _fill(temp)
        problems = _check_file(temp)
        if problems:
            _remove_temp(temp)
            return problems
        os.replace(temp, path)
    except BaseException:  # also Ctrl-C
        _remove_temp(temp)
        raise
    # The old database's journal must not be replayed into the new file.
    for file in _side_files(path):
        file.unlink(missing_ok=True)
    return []


def _check_file(path: Path) -> list[str]:
    conn = sqlite3.connect(path)
    try:
        return check(conn)
    finally:
        conn.close()


def _fill(path: Path) -> None:
    conn = sqlite3.connect(path)
    try:
        # The schema file also turns on foreign keys for this connection.
        conn.executescript(SCHEMA_FILE.read_text(encoding="utf-8"))
        with conn:
            load_lookups(conn)
            generator = Generator(conn)
            generator.add_languages_and_staff()
            generator.add_demo_experts()
            generator.add_random_experts()
    finally:
        conn.close()


def print_summary(conn: sqlite3.Connection) -> None:
    print("Experts per status:")
    for status, count in conn.execute(
        "SELECT Status, COUNT(*) FROM Candidates GROUP BY Status ORDER BY Status"
    ):
        print(f"  {STATUS_NAMES.get(status, f'Unknown ({status})'):<18} {count}")
    print("Demo logins:")
    for email in DEMO_EMAILS:
        print(f"  expert      {email}")
    for (email,) in conn.execute("SELECT email FROM ss_staff_users ORDER BY id"):
        print(f"  secretariat {email}")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Build the test database.")
    parser.add_argument("--reset", action="store_true", help="replace the database if it exists")
    args = parser.parse_args(argv)

    path = database_path()
    if path.exists() and not args.reset:
        print(f"{path} already exists. Use --reset to delete and rebuild it.")
        return 1

    try:
        problems = build(path)
    except PermissionError as error:
        # On Windows this usually means the app or a database browser has the file open.
        print(
            f"Could not write {path}: {error}\n"
            "The old database is unchanged. Close anything that has it open and try again."
        )
        return 1
    if problems:
        for problem in problems:
            print(problem)
        print(f"check_data: {len(problems)} problem(s). The old database is unchanged.")
        return 1

    print(f"Built {path}")
    conn = sqlite3.connect(path)
    try:
        print_summary(conn)
    finally:
        conn.close()
    print("check_data: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
