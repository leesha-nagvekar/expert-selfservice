# Decisions

Short records of choices made in the project and why.

## Stack changed to ASP.NET Core (2 Nov 2026)

**Decision (Claus, guide phase 3, 2 Nov 2026):** from phase 3 the app is built in ASP.NET Core MVC on .NET 10 (LTS) with Razor views, SQLite through Microsoft.Data.Sqlite and Dapper, plain CSS, xUnit and `dotnet format`. It replaces the Python choice of 30 Oct (below).

**Why:**
- .NET is Leesha's own home ground. The guide's rule 4 is "never accept code you can't explain", and that is easier in a language she already knows.
- Gate 3 (20 Nov) now includes walking Claus through the code, and the T3 retake is in C#.

**What stays:**
- The test data tools stay in Python: `seed.py`, `check_data.py`, `rules.py`, `settings.py`, their 74 pytest tests and Ruff. `python seed.py --reset` is still the reset command.
- `db/schema_sqlite.sql` stays the one source of the table structure. The .NET app reads the same database file.
- `i18n/da.json`, `i18n/en.json`, the colour tokens, fonts and logos were copied from the Python skeleton (commit `4c7761a`) and move to `wwwroot/` in the .NET skeleton.
- The Python skeleton is kept under the Git tag `python-skeleton` (branch `phase3-stack-decision`) for reference. It is not merged.

**Rules carried over, in .NET form:**
- Every query uses Dapper parameters (`@name`), never SQL built from text. Foreign keys are turned on for every connection (`Foreign Keys=True` in the connection string).
- CSRF: ASP.NET Core's built-in antiforgery tokens on every POST form, switched on for all of them, not one by one. It replaces the own helper planned for Python.
- Login: the session cookie is `HttpOnly` and `SameSite=Lax`, and holds only `expert_id` or `staff_id`.
- One-time codes come from `RandomNumberGenerator`, never `Random`, and are stored as HMAC-SHA256 with a secret key.
- The app listens on `127.0.0.1` only. Own "Page not found" and error pages, never the developer exception page or a stack trace.
- All text comes from `i18n/*.json` through a small service of our own, not `.resx` files, because the guide names the JSON files.
- Times are shown in Europe/Copenhagen with .NET's own `TimeZoneInfo`, so no extra package is needed.
- Every NuGet package is pinned to an exact version and gets one line here explaining why it is needed. The list is written in the skeleton step.

**Open points:**
- This machine has only the .NET 7.0.100 SDK. The .NET 10 SDK must be installed before the skeleton step, and the README must say so.
- Where secrets go: CLAUDE.md ground rule 4 says `.env`, but .NET doesn't read `.env` on its own. Either a small package reads it, or .NET's "user secrets" are used (also outside Git). Decided with E1 login, the first feature with a secret.

## One command for all tests: `.\test.ps1` (3 Nov 2026)

**Decision (Leesha):** `test.ps1` in the repo root runs every test and check and stops at the first failure: pytest, `ruff check`, `ruff format --check`, and (from the skeleton step) `dotnet test` and `dotnet format --verify-no-changes`. Exit code 0 means all passed.

**Why:**
- The guide's MVP checklist says "All tests run with one command and pass". The app is tested with `dotnet test` and the test data tools with pytest, so a small script ties them together.
- The script is a few plain lines anyone can read and explain.
- Not chosen: an MSBuild step that makes `dotnet test` call pytest. It gives one familiar command, but the logic would be hidden in the `.csproj` and be harder to explain.

## Stack, first choice: Python (30 Oct 2026, replaced 2 Nov 2026)

**Choice (Leesha):** Python, FastAPI, Jinja2 templates, SQLite (Python's built-in `sqlite3`), plain CSS, pytest and Ruff.

**Why:**
- It is the guide's default, so it needs no extra approval from Claus.
- Phase 2 is already written in Python with pytest and Ruff. `rules.py`, `settings.py` and the 74 tests are reused as they are, and there is only one language to learn.
- SQLite is one file with nothing to install, so someone else can set the app up from the README in under 15 minutes. The app only runs on localhost with 100 test experts, so a database server would add work without any benefit.
- FastAPI's `TestClient` tests every page without a running server, and Jinja2 builds the HTML on the server, so no JavaScript framework is needed.

**Options considered:**
- **Flask + Jinja2:** a close second and a little easier to learn. Not chosen: it leaves the default for a small gain (built-in CSRF), which a small helper gives us in FastAPI.
- **Django:** the strongest security defaults. Not chosen: its ORM and migrations clash with the hand-written `db/schema_sqlite.sql`, its translations use gettext files instead of `i18n/*.json`, and it is the most to learn in four weeks.
- **Node.js (Express):** left out. It would throw away the Phase 2 Python code and tests.

**Packages.** Every version is pinned exactly in `requirements.txt`, so the install gives the same result on any machine.

| Package | Why it is needed |
|---|---|
| fastapi | The web framework: routes, forms and dependencies such as "the logged-in expert". |
| uvicorn | The server that runs the app on 127.0.0.1. Plain install, without the `[standard]` extras, so nothing needs compiling on Windows. |
| jinja2 | Builds the HTML pages on the server, with auto-escaping. |
| python-multipart | FastAPI needs it to read HTML form posts. |
| itsdangerous | Starlette's `SessionMiddleware` needs it to sign the session cookie. |
| httpx | FastAPI's `TestClient` needs it to call pages in tests. |
| tzdata | Windows has no time-zone database, so `zoneinfo` needs it to show times in Europe/Copenhagen. |
| python-dotenv | Reads `.env` (database path, session secret, code key). |
| Faker | Makes the 100 fake experts in `seed.py`. |
| pytest | Runs all tests with one command. |
| ruff | Formatter and linter. |

Python's standard library covers the rest: `sqlite3`, `secrets`, `hmac`/`hashlib`, `json` and `zoneinfo`. `starlette` and `pydantic` come with fastapi.

**Rules that come with the choice:**
- Plain `def` routes, no `async`. Every query uses `sqlite3` with `?` parameters. No ORM, so `db/schema_sqlite.sql` stays the one source of the table structure. `PRAGMA foreign_keys = ON` on every connection.
- CSRF: our own small helper instead of `starlette-csrf`. A random token from `secrets` is kept in the session, every POST form sends it in a hidden field, and the server compares it with `hmac.compare_digest`. A missing or wrong token gives a plain error page.
- Session: Starlette's `SessionMiddleware`, with the secret from `.env`, `httponly` and `samesite=lax`. It holds only `expert_id` or `staff_id`.
- One-time codes come from `secrets` and are stored as HMAC-SHA256 with a key from `.env`. A plain hash is not enough: a 6-digit code has only a million values, so trying them all reverses a plain hash in a second.
- Own "Page not found" and error pages, never a stack trace. uvicorn listens on 127.0.0.1 only.
- All text through a `t("key")` function in the templates, reading `i18n/da.json` and `i18n/en.json`. Dates are formatted by a small helper with month names from the same files, so no Babel package is needed.

## 2 Nov 2026: CVR stored in ss_expert_company

**Decision (Leesha):** the CVR number stays in the side table `ss_expert_company.cvr`, not in a new column in Candidates.

**Why:** guide phase 1, step 3: what is missing goes into new tables with the prefix ss_, and the original tables stay untouched. VBA's schema has no CVR column, so a CVR column in Candidates would change an original table. That option is out, so there is nothing to ask Claus.

## Phase 2: test database (29 Oct 2026)

**Hand-written SQLite schema (`db/schema_sqlite.sql`).** `schema/schema.sql` must never go into Git, but anyone who clones the repo must be able to run `python seed.py --reset`. So the 8 tables the app uses are written out by hand, with the same table and column names. Types are mapped to SQLite: int → INTEGER, text → TEXT, bit → INTEGER 0/1, date → TEXT. Text lengths aren't enforced by SQLite. The app's validation and `check_data.py` enforce them. Fags has no primary key in the original, so it has none here either.

**Same data every run.** Faker `da_DK` and `random` both use seed 2026, and all dates are counted from a fixed reference date (2026-10-01), not today. Faker is pinned to an exact version in `requirements.txt`, because a new Faker version gives different names from the same seed. A test seeds twice and compares the two databases.

**Lookup lists in `data/lookups/`.** VBA's export (28 Oct) was a sample with no personal data. Regions and Fags are copied as they are. ProfessionTags has 3 made-up roles added, because the export was only a sample. Danish postcodes come from GeoNames (CC BY 4.0). The government postcode API DAWA closed in 2026, and its replacement (Datafordeler) needs an API key, which would be a secret to manage. `tools/build_postcodes.py` fetched GeoNames once. The reset doesn't use the internet. Danish postcode rows start at Id 2001, so they never clash with the exported Ids.

**97 random + 3 demo experts = 100.** The demo experts have Id 1–3 and fixed values, so the demo script never changes. Anna starts at 2100 København Ø so the demo can change her postcode to 8000 Aarhus C.

**RegistrationId from 900001 up.** Made-up numbers in that range are easy to tell apart from real VBA numbers.

**Languages in new tables.** The schema has no language column. `ss_languages` and `ss_expert_languages` hold them, ready for story E8.

**`seed.py` refuses to run without `--reset` when the database exists**, so nobody wipes data by mistake.

**Fake numbers may match real ones.** Phone numbers and CVR numbers in valid formats may belong to a real person or company by chance. That is acceptable: the app never sends SMS, all emails end in @example.com, and CVR is public company data.

## Code review, Phase 2 (29 Oct 2026)

`/code-review` is Claude Code's automated review. It reads the changed code and reports likely bugs and places where the code can be simpler or safer. It ran three times on the Phase 2 code (`seed.py`, `check_data.py`, `settings.py`, `rules.py`, the tests and `tools/`) before the first commit. The MVP checklist says every finding must be fixed or explained here, so all 29 are listed below. After round 3 we stopped: the findings had become edge cases, and further rounds were giving less and less.

Overall result after the three rounds:
- Rounds 1 and 2 left the generated test data **byte-for-byte the same** (SHA-256 of a full database dump: `7dc6cf3976bc4349e2e686705cf6e6594dd8489b0d86d3fd1a3f17c8849843fd`). Those fixes changed how the code is built, not what it produces.
- Round 3 changed the data **on purpose, in one place only**: the end dates of the 10 random Standby experts (round 3 #3). A line-by-line comparison of the two dumps shows exactly 10 changed rows, and in each only `UntilDate` differs. New hash: `daacea2c04e7d39f8ac888289d3767e139730cdff82db72e5833acfd4893864d`.
- 74 tests pass. `ruff check` and `ruff format --check` pass.

### Round 1: 9 findings, all fixed

| # | Finding | Why it mattered | Fix | Proved by |
|---|---|---|---|---|
| 1 | `python check_data.py data/expert_register.db` crashed. The code turned the path into a `file:` link, which only works for full paths. | The checker failed with a traceback instead of checking anything. | The path is turned into a full path first (`Path.resolve()`). | `test_main_accepts_relative_path` |
| 2 | If a build failed halfway, a half-built database was left behind. | `--reset` deleted the old database first. A crash then left a broken file, and the next run refused with "already exists". The app could also open the broken file. | The build writes to a temp file `expert_register.building.db` and only replaces the real database (`os.replace`) after it succeeded. On any error, including Ctrl-C, the temp file is deleted. If Windows has the database locked (the app or a DB browser is open), `seed.py` says so and returns 1. The old database is never touched. | `test_failed_build_keeps_old_database`, `test_failed_build_leaves_no_file`, `test_reset_on_locked_file_keeps_old_database`, `test_build_removes_leftover_temp_file`. Also tested by hand with a real lock: another process held the database open during `--reset`. |
| 3 | The `.env` reader didn't handle quotes (`DATABASE_PATH="…"`), or the invisible marker (BOM) Notepad and PowerShell can put at the start of a file. | With quotes the path was invalid on Windows. With a BOM the setting was silently ignored, so `--reset` would have rebuilt the default database instead of the one you meant. | The hand-written reader is replaced by **python-dotenv**, reading the file as `utf-8-sig`. Quotes, comments and `export` work. | `test_env_file_with_bom_and_quotes`, `test_bom_on_first_line`, `test_environment_wins_over_env_file`, `test_default_without_env_file` |
| 4 | The postcode check only asked for 4 digits. It didn't check that the postcode is Danish. | Norway also has 4-digit postcodes, and the lookup list has Norwegian cities. A Norwegian postcode would have passed. | The check also requires the city's region to be one of the 5 Danish regions (`DANISH_REGION_IDS`). | `test_catches_four_digit_postcode_outside_denmark` |
| 5 | The summary after a reset assumed the demo experts are Id 1–3, and crashed on an unknown status. | If the insert order changed, random experts would be shown as demo logins. A strange status would crash `seed.py` after the database was already built. | The demo logins are listed from `DEMO_EMAILS`. An unknown status is shown as "Unknown (n)". | Checked by every `seed.py --reset` run |
| 6 | The CVR check-digit maths was written twice, in `seed.py` and in `check_data.py`. | If one copy changed, the seed could make CVR numbers that its own checker rejects. | `seed.py` draws 7 digits and tries check digits 0–9 with `cvr_is_valid`. The formula exists only once. Same random draws, so the same CVR numbers. | `test_same_data_every_run`, plus the identical dump hash |
| 7 | Demo emails and status codes were defined in both `seed.py` and `check_data.py`. | Renaming a demo email in one file would make every fresh seed fail its own check. | New module **`rules.py`** holds them once (see the design decisions below). | `test_same_data_every_run`, `test_fresh_seed_passes_check_data` |
| 8 | No length check on Company, Designation, the phones or staff emails. | SQLite doesn't enforce text lengths. Too-long test data would only show up later, in the app's forms or when copying back to the real schema. | `rules.MAX_LENGTHS` holds every limit from `schema.sql`. One check loops over it, and staff emails are checked too. | `test_catches_too_long_text`, `test_catches_too_long_staff_email` |
| 9 | `PRAGMA foreign_keys = ON` (the setting that turns on key checks) was set both in the schema file and in `seed.py`. | It was unclear which one was needed. Removing the wrong one could silently turn off the key checks. | It is now only in `db/schema_sqlite.sql`. | `test_schema_file_turns_on_foreign_keys` |

### Round 2: 10 findings, 8 fixed, 2 explained

| # | Finding | Why it mattered | Fix | Proved by |
|---|---|---|---|---|
| 1 | An empty (NULL) RegistrationId crashed the checker: `sorted()` can't compare `None` with text. | The checker broke on exactly the bad data it is meant to report. | An empty RegistrationId is still reported as "not a test number", but left out of the duplicate count. | `test_missing_registration_id_is_reported_not_crashed` |
| 2 | Only the temp database was cleaned up, not SQLite's side files (`-journal`, `-wal`, `-shm`). | If a build is killed hard (window closed, Task Manager), SQLite can leave a `-journal` file. The next build would replay it into its new file and fail. | `_remove_temp()` deletes the temp file and all three side files, both at the start of a build and after an error. | `test_build_removes_leftover_side_files`. It uses dummy leftover files. A real killed build was not reproduced. |
| 3 | The check accepted 8–12 experts without a phone, but SPEC section 7 says exactly 10. | A seed change could drift without the check noticing. The number 10 was also split across two files. | `rules.NO_PHONE_EXPERTS = 10`. The seed works out its random ones from it (10 minus Bo = 9), and the check requires exactly 10. | `test_catches_wrong_number_without_phone`, `test_no_phone_count_matches_rule` |
| 4 | The role check only asked for at least 1 role. The SPEC says 1–2, and a duplicate role wasn't caught. | Wrong role data would pass `check_data` with "OK". | `rules.ROLES_PER_EXPERT = (1, 2)`, next to `SKILLS_PER_EXPERT = (1, 4)`. One helper (`_check_per_expert`) checks both the count range and duplicates, for skills and roles alike. **Not done:** the exact status counts (Active 53 etc.) are not checked. `check_data` checks whether the data is valid, not how it is spread (decided with Leesha). | `test_catches_three_roles`, `test_catches_same_role_twice` |
| 5 | The postcode rule was written as SQL in both `seed.py` and `check_data.py`. | That broke the "one place" rule just introduced with `rules.py`. If the rule changed in one file only, the seed could pick postcodes that the check rejects. | `rules.DANISH_POSTCODE_SQL` holds the SQL condition once, and both files use it. | `test_catches_four_digit_postcode_outside_denmark`, plus the identical dump hash |
| 6 | `.gitignore` didn't ignore SQLite's side files. | Such a file (see #2, or the app later) could end up in Git with database content in it. | `*.db-journal`, `*.db-wal`, `*.db-shm` added. | Checked with `git check-ignore` |
| 7 | The temp file kept the extension of `DATABASE_PATH`, so it didn't always end in `.db`. | With e.g. `register.sqlite3`, the temp file wouldn't be ignored by Git. | The temp file is always `<name>.building.db`. | `test_temp_file_always_ends_in_db` |
| 8 | An empty (NULL) skill was also reported as "the same skill twice". | A false message sends whoever is debugging in the wrong direction. | The duplicate check counts only filled-in skills (`COUNT(s.Skill)`). | `test_empty_skill_is_not_reported_as_duplicate` |
| 9 | A path given to `check_data.py` is read from the current folder, but `DATABASE_PATH` is read from the project folder. | The same text can point to two different files. | **Explained, no code change.** Reading command-line paths from the current folder is how every command-line tool works. `DATABASE_PATH` must not depend on where you start the app. The README now says this in one sentence. | – |
| 10 | `tools/build_postcodes.py` drops every row with Id ≥ 2001, and doesn't write its two files atomically. | A later VBA export with such Ids could be overwritten, and a crash could leave the two files out of step. | **Explained, no change.** It is a one-off tool, and replacing the rows from 2001 up is its documented job. VBA's exported rows sit below 2001. The download finishes before anything is written, so a network error can't leave half-written files. If VBA sends a full lookup export later, the lookup files are rebuilt by hand anyway. | – |

### Round 3: 10 findings, 7 fixed, 3 explained

| # | Finding | Why it mattered | Fix | Proved by |
|---|---|---|---|---|
| 1 | `--reset` replaced the database file but left the **old** database's side files (`-journal`, `-wal`, `-shm`) next to it. | If the app or a database browser had crashed earlier, SQLite could replay the old journal into the fresh database and corrupt it. | After a successful replace, `seed.py` deletes the target's side files: they belong to the old database. The helper `_side_files()` is shared with the temp-file cleanup. | `test_build_removes_old_side_files` |
| 2 | The data check ran **after** the old database was replaced. | A build with bad data (e.g. a lookup file changed) would still overwrite a good database, which broke the promise that a failed reset leaves the old database alone. | `build()` runs `check_data` on the temp file first. Only data that passes replaces the old database. Otherwise the problems are printed, the old database stays, and the exit code is 1. | `test_failed_check_keeps_old_database` |
| 3 | Standby end dates were counted from the reference date (1 Oct 2026), so some could fall before the hand-over demo on 27 Nov 2026. | A Standby expert with an end date in the past is inconsistent data in the live demo. With this seed, the earliest date was in fact 2027-01-04, so it was a risk, not a real problem yet. | New `DEMO_DATE = 2026-11-27`. Standby end dates are now 30–365 days after it, which guarantees it. Same random draws, so only these 10 dates changed (see the overall result above). | `test_standby_ends_after_demo` |
| 4 | `.gitignore` didn't ignore `*.sqlite3`. | With `DATABASE_PATH=…/register.sqlite3`, the test database could be committed. | `*.sqlite3` added. | – |
| 5 | `tools/build_postcodes.py` drops rows with Id ≥ 2001, and crashes on a blank line in a CSV. | Same as round 2 #10, plus the blank-line case. | **Explained, no change.** See round 2 #10. The tool reads only CSV files it wrote itself, and those never contain blank lines. | – |
| 6 | The demo-email lookup cared about upper/lower case; the rest of the checker doesn't. | After a later email change to `Anna.Demo@example.com`, the checker would wrongly report Anna as missing. | `WHERE lower(Email) = lower(?)`. | `test_demo_email_found_in_any_case` |
| 7 | The email check only looked at the ending, so `a@b@example.com` or `x y@example.com` passed. | Such an address can't be used to log in, and the check gave false confidence. | `rules.is_valid_email()`: one `@`, no spaces, a name part of letters, digits and `._%+-`, a domain with a dot. `check_data` uses it now, and the app will use it for screen X6 in Phase 3. | `test_catches_malformed_email`, `tests/test_rules.py` |
| 8 | No check that every expert has at least one language, or that a CVR always comes with a company name. | Inconsistent data could pass. | **Explained, no change.** Neither is a rule in the SPEC: languages belong to E8 (not specified yet), and screen X8 allows a CVR without a company name. `check_data` checks the SPEC's rules, the same reason the status counts aren't checked (round 2 #4). | – |
| 9 | Tests hard-coded values that `rules.py` defines once (10 experts without a phone). | Changing the rule would break correct tests. | The phone tests use `rules.NO_PHONE_EXPERTS`. **Not changed:** the fixed Ids in tests (Anna = 1, role Skønsmand = 35). They are known demo data, explained in comments next to them. | `test_no_phone_count_matches_rule`, `test_catches_wrong_number_without_phone` |
| 10 | `tools/build_postcodes.py` works out its own lookups path instead of importing `settings.ROOT`. | Two places know where the lookup files are. | **Explained, no change.** It is a stand-alone one-off tool. Tying it to the app's modules gains nothing, and the folder is unlikely to move. | – |

### Design decisions that came out of the review

**One place for the data rules: `rules.py`.** Several findings had the same cause: one rule written in two files (round 1 #6 and #7, round 2 #3 and #5). `rules.py` now holds every data rule once: status codes and names, demo emails, expected counts, the number of experts without a phone, skills and roles per expert, Danish region Ids and the postcode SQL, the column length limits from `schema.sql`, and the CVR check. `seed.py` and `check_data.py` import from it, so the seed and the check can't drift apart. In Phase 3 the app will use the same module for its form validation, such as CVR and length limits.

**Rebuild through a temp file.** `seed.py --reset` must never leave you without a working database before a demo. So the new database is built next to the old one and checked with `check_data`. Only if it passes does an atomic `os.replace` put it in place. A failed, interrupted or failing reset leaves the old database as it was. The temp file and its side files are cleaned up. On Windows, close the app before resetting: an open database can't be replaced, and `seed.py` says so.

**python-dotenv reads `.env`.** A small library is safer than a hand-written parser for a file people edit by hand. It handles quotes, comments and `export`. The file is read as `utf-8-sig`, because Notepad and PowerShell may add a BOM. The order is: environment variable, then `.env`, then the default. Phase 3 needs python-dotenv anyway, for the session secret.

**Foreign keys are per connection.** SQLite doesn't store `PRAGMA foreign_keys = ON` in the database file. It applies only to the connection that ran it. `db/schema_sqlite.sql` turns it on for the build. **Action for Phase 3: the app must turn it on for every connection it opens.** Otherwise the database won't stop broken keys.

## Security review, Phase 2 (29 Oct 2026)

`/security-review` is Claude Code's security-focused review. Unlike `/code-review`, it looks only for weaknesses an attacker could exploit, such as injection, leaked secrets, unsafe file handling or exposed personal data. It reports only findings it is confident about (8 out of 10 or higher), to avoid false alarms.

**How it was run.** The command reviews commits that are not yet on the remote. So the Phase 2 work was first committed locally, without pushing: `f5c1d31` (spec documents) and `e35def9` (test database). Then `origin/HEAD` was set with `git remote set-head origin --auto`, and the review ran on those two commits. Nothing was pushed before the review was done. `schema/` was kept out of the review.

**Result: no findings.** Nothing reached the reporting bar, so there was nothing to fix. The review lists every area it checked and why each one is safe today:

| Area | What was checked | Why it is safe |
|---|---|---|
| SQL injection | Every query in `seed.py`, `check_data.py` and `rules.py` | All values go in as `?` parameters. Table and column names that are written into SQL come only from constants (`CANDIDATE_COLUMNS`, `LOOKUP_TABLES`, `MAX_LENGTHS`, and `DANISH_POSTCODE_SQL`, which holds whole numbers only) or from the headers of our own committed lookup files. No outside input reaches any SQL, because there is no web app yet. |
| Running code or commands | `subprocess`, `os.system`, `eval`, `exec`, pickle, yaml | None of them is used. |
| File paths | Where the database is written and read | The path comes only from `DATABASE_PATH`, which is trusted configuration, not user input. `check_data.py` opens the database read-only (`mode=ro`). |
| `tools/build_postcodes.py` | Download and unzip | It downloads over HTTPS, and Python checks the certificate. It reads only `DK.txt` from the zip, in memory, and never unpacks files to disk, so a crafted zip can't write elsewhere. It writes only to the two fixed lookup files. |
| Secrets | Code, lookup files, Git | `.env` and `.env.*` are ignored by Git. `.env.example` holds only `DATABASE_PATH`. There is no password, key or token anywhere. Git tracks no `.env`, `.db` or `schema/` file. |
| Randomness | `random.Random(2026)` in `seed.py` | It only makes repeatable fake data, which is its purpose. It isn't used for anything that must be secret. |
| Personal data | Expert, demo and staff rows; lookup files | All experts are made up with Faker. Every email ends in `@example.com`, and `check_data` enforces that. The lookup files hold regions, cities, postcodes, fields of expertise and roles, with no rows about people. The only other email in the code is `someone@gmail.com`, in a test that proves the check catches it. It is never sent anywhere. |
| Database schema | `db/schema_sqlite.sql` | Only table definitions, `PRAGMA foreign_keys = ON` and the two triggers that block edits and deletes in `ss_change_log`. No `ATTACH` and no `load_extension`. |
| Network | Anything that listens or sends | Nothing listens on a network, and nothing sends email or SMS. |

**Why there was so little to find.** Phase 2 has no user input: no web pages, no forms and no login. The dangerous parts of this project start in Phase 3. The rules below come from this review and from the SPEC, and are for Phase 3:

- **One-time login codes must use Python's `secrets` module, never `random`.** `random` can be predicted, so codes made with it could be guessed. The review raised this. The fixed-seed `random` stays only in `seed.py`.
- **Every query keeps using `?` parameters** for anything a user typed. Never build SQL from form or URL values (the guide's review checklist shows exactly this mistake).
- **Turn on foreign keys for every connection** (see "Foreign keys are per connection" above).
- **Store only a hash of each code** (`ss_login_codes.code_hash`, SPEC section 5), and take `expert_id` only from the session, never from the URL (SPEC section 2).

`/security-review` runs again on the Phase 3 code, where it has real input to examine.
