# Expert Register Self-Service (MVP)

Lets VBA's experts (skønsmænd) update their own details. Test data only: 100 fake experts, every email ends in @example.com. It runs on your own machine only.

## Install

Needs Python 3.12.

```
python -m venv .venv
.venv\Scripts\activate          # macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
copy .env.example .env          # macOS/Linux: cp .env.example .env
```

## Reset the test database

```
python seed.py --reset
```

This builds `data/expert_register.db` again with the same 100 fake experts every time, then checks the data. Run it before every demo. Close the app first: on Windows an open database can't be replaced, and the reset will tell you so. If a reset fails, or the new data fails the checks, the old database stays as it was. Without `--reset`, the script refuses to overwrite an existing database.

Demo logins:

| Who | Email |
|---|---|
| Expert, Active | anna.demo@example.com |
| Expert, Standby, no phone | bo.demo@example.com |
| Expert, Accepted | carl.demo@example.com |
| Secretariat | sofie.staff@example.com |
| Secretariat | mads.staff@example.com |

## Check the data

```
python check_data.py                  # the database from .env (or the default)
python check_data.py path/to/other.db # any other database file
```

A path given to `check_data.py` is read from the folder you are in. `DATABASE_PATH` in `.env` is read from the project folder.

Checks for exactly 100 experts, no duplicate emails, valid keys, @example.com everywhere, and valid CVR numbers. Exit code 0 means OK.

## Test

```
pytest
ruff check . && ruff format --check .
```

## Files

- `db/schema_sqlite.sql`: the database structure.
- `data/lookups/`: regions, cities, postcodes, fields of expertise (Fags) and roles.
- `seed.py`, `check_data.py`: build and check the test database.
- `rules.py`: the data rules both use (statuses, demo emails, length limits, CVR check).
- `settings.py`: reads `DATABASE_PATH` from the environment or `.env`.
- `tools/build_postcodes.py`: a one-off that fetched the Danish postcodes (GeoNames, CC BY 4.0).
- `docs/`: specification, data dictionary, stories and decisions.
