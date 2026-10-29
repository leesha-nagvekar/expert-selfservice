-- SQLite test database for Expert Register Self-Service.
--
-- Part 1: the 8 tables the app uses from VBA's database (SPEC decision 8),
-- with the same table and column names as schema/schema.sql.
-- Type mapping: int -> INTEGER, (n)varchar -> TEXT, bit -> INTEGER 0/1,
-- date -> TEXT 'YYYY-MM-DD'. SQLite doesn't enforce text lengths, so the app
-- and check_data.py do.
--
-- Part 2: the new self-service tables (prefix ss_), SPEC section 5.
-- All ss_ times are UTC, stored as TEXT 'YYYY-MM-DDTHH:MM:SSZ'.

PRAGMA foreign_keys = ON;

-- ---------------------------------------------------------------------------
-- Part 1: tables from VBA's database
-- ---------------------------------------------------------------------------

CREATE TABLE Regions (
    Id             INTEGER PRIMARY KEY,
    RegionName     TEXT    NOT NULL,
    ShowInFilter   INTEGER,
    IsAutoSelected INTEGER,
    DisplayOrder   INTEGER NOT NULL DEFAULT 1000
);

CREATE TABLE Cities (
    Id       INTEGER PRIMARY KEY,
    City     TEXT    NOT NULL,
    RegionId INTEGER NOT NULL REFERENCES Regions (Id),
    ZipCode  TEXT    NOT NULL
);

-- Zipcodes.CityId has no foreign key in the original schema.
CREATE TABLE Zipcodes (
    Id      INTEGER PRIMARY KEY,
    CityId  INTEGER,
    Zipcode TEXT    NOT NULL
);

CREATE TABLE Candidates (
    Id             INTEGER PRIMARY KEY,
    FirstName      TEXT    NOT NULL,
    LastName       TEXT    NOT NULL,
    RegistrationId TEXT,
    PhoneHome      TEXT,
    PhoneWork      TEXT,
    PhoneMobile    TEXT,
    Address        TEXT,
    ZipcodeId      INTEGER REFERENCES Zipcodes (Id),
    Notes          TEXT,
    Company        TEXT,
    Designation    TEXT,
    Website        TEXT,
    statusOld      INTEGER,
    LastUpdated    TEXT,
    Email          TEXT,
    Fax            TEXT,
    Street         TEXT,
    Stat_Provins   TEXT,
    Ratings        INTEGER,
    Bygherre       INTEGER,
    "Rådgiver"     INTEGER,
    "Entreprenør"  INTEGER,
    Created        TEXT,
    LastAccessed   TEXT,
    Status         INTEGER NOT NULL DEFAULT -1,
    FirstName2     TEXT,
    LastName2      TEXT,
    MiddleName     TEXT,
    FirstNameOld   TEXT,
    LastNameOld    TEXT,
    UntilDate      TEXT,
    OldStatus      INTEGER
);

-- Fags has no primary key in the original schema, so it has none here.
CREATE TABLE Fags (
    Id  INTEGER NOT NULL,
    Fag TEXT    NOT NULL
);

-- Skill holds the Fags.Fag name as text. There is no foreign key to Fags.
CREATE TABLE CandidateSkills (
    Id          INTEGER PRIMARY KEY,
    CandidateId INTEGER NOT NULL REFERENCES Candidates (Id),
    Skill       TEXT
);

CREATE TABLE ProfessionTags (
    Id           INTEGER PRIMARY KEY,
    Tag          TEXT    NOT NULL,
    ShowInFilter INTEGER
);

CREATE TABLE CandidateTags (
    Id          INTEGER PRIMARY KEY,
    CandidateId INTEGER NOT NULL REFERENCES Candidates (Id),
    TagId       INTEGER NOT NULL REFERENCES ProfessionTags (Id)
);

-- ---------------------------------------------------------------------------
-- Part 2: self-service tables
-- ---------------------------------------------------------------------------

CREATE TABLE ss_staff_users (
    id         INTEGER PRIMARY KEY,
    name       TEXT NOT NULL,
    email      TEXT NOT NULL UNIQUE,
    created_at TEXT NOT NULL
);

CREATE TABLE ss_login_codes (
    id         INTEGER PRIMARY KEY,
    email      TEXT NOT NULL,
    purpose    TEXT NOT NULL CHECK (purpose IN ('login', 'email_change')),
    code_hash  TEXT NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    used_at    TEXT
);

CREATE TABLE ss_login_lockouts (
    email        TEXT PRIMARY KEY,
    failed_count INTEGER NOT NULL DEFAULT 0,
    locked_until TEXT
);

CREATE TABLE ss_dev_mailbox (
    id         INTEGER PRIMARY KEY,
    to_email   TEXT NOT NULL,
    subject    TEXT NOT NULL,
    body       TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE TABLE ss_email_changes (
    id           INTEGER PRIMARY KEY,
    candidate_id INTEGER NOT NULL REFERENCES Candidates (Id),
    new_email    TEXT    NOT NULL,
    created_at   TEXT    NOT NULL,
    confirmed_at TEXT,
    cancelled_at TEXT
);

CREATE TABLE ss_expert_company (
    candidate_id INTEGER PRIMARY KEY REFERENCES Candidates (Id),
    cvr          TEXT
);

CREATE TABLE ss_pending_changes (
    id           INTEGER PRIMARY KEY,
    candidate_id INTEGER NOT NULL REFERENCES Candidates (Id),
    field        TEXT    NOT NULL
                 CHECK (field IN ('FirstName', 'MiddleName', 'LastName', 'Skills')),
    old_value    TEXT,
    new_value    TEXT,
    status       TEXT    NOT NULL DEFAULT 'pending'
                 CHECK (status IN ('pending', 'approved', 'rejected')),
    reason       TEXT,
    created_at   TEXT    NOT NULL,
    decided_by   INTEGER REFERENCES ss_staff_users (id),
    decided_at   TEXT,
    CHECK (status <> 'rejected' OR (reason IS NOT NULL AND reason <> ''))
);

CREATE TABLE ss_change_log (
    id                INTEGER PRIMARY KEY,
    candidate_id      INTEGER NOT NULL REFERENCES Candidates (Id),
    actor_type        TEXT    NOT NULL CHECK (actor_type IN ('expert', 'staff')),
    actor_id          INTEGER NOT NULL,
    field             TEXT    NOT NULL,
    old_value         TEXT,
    new_value         TEXT,
    pending_change_id INTEGER REFERENCES ss_pending_changes (id),
    created_at        TEXT    NOT NULL
);

-- The change log is append-only (story S3).
CREATE TRIGGER ss_change_log_no_update
BEFORE UPDATE ON ss_change_log
BEGIN
    SELECT RAISE(ABORT, 'ss_change_log is append-only');
END;

CREATE TRIGGER ss_change_log_no_delete
BEFORE DELETE ON ss_change_log
BEGIN
    SELECT RAISE(ABORT, 'ss_change_log is append-only');
END;

CREATE TABLE ss_languages (
    id   INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE ss_expert_languages (
    candidate_id INTEGER NOT NULL REFERENCES Candidates (Id),
    language_id  INTEGER NOT NULL REFERENCES ss_languages (id),
    PRIMARY KEY (candidate_id, language_id)
);
