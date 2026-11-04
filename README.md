# Expert Register Self-Service (MVP)

Lets VBA's experts (skønsmænd) update their own details. Test data only: 100 fake experts, every email ends in @example.com. It runs on your own machine only.

The app so far is the skeleton: the VBA top bar, the language switch and the error pages. It doesn't use the database yet.

## Install

Needs:

- Python 3.12
- .NET SDK 10.0.403, or a later 10.0.4xx patch ([download](https://dotnet.microsoft.com/download/dotnet/10.0)). `global.json` refuses any other version.

```
python -m venv .venv
.venv\Scripts\activate          # macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
copy .env.example .env          # macOS/Linux: cp .env.example .env
python seed.py --reset
```

If Windows blocks the activate script, run `Set-ExecutionPolicy -Scope Process Bypass` in the same window first.

## Run

```
dotnet run --project src/ExpertSelfService
```

Open http://127.0.0.1:5080. Stop the app with Ctrl+C. It listens on this machine only.

## Reset the test database

With the virtual environment activated:

```
python seed.py --reset
```

This builds `data/expert_register.db` again with the same 100 fake experts every time, then checks the data. Run it before every demo. Close the app first: on Windows an open database can't be replaced, and the reset will tell you so. If a reset fails, or the new data fails the checks, the old database stays as it was. Without `--reset`, the script refuses to overwrite an existing database.

Demo logins (login comes with story E1):

| Who | Email |
|---|---|
| Expert, Active | anna.demo@example.com |
| Expert, Standby, no phone | bo.demo@example.com |
| Expert, Accepted | carl.demo@example.com |
| Secretariat | sofie.staff@example.com |
| Secretariat | mads.staff@example.com |

## Check the data

With the virtual environment activated:

```
python check_data.py                  # the database from .env (or the default)
python check_data.py path/to/other.db # any other database file
```

A path given to `check_data.py` is read from the folder you are in. `DATABASE_PATH` in `.env` is read from the project folder.

Checks for exactly 100 experts, no duplicate emails, valid keys, @example.com everywhere, and valid CVR numbers. Exit code 0 means OK.

## Test

Close the app first, then:

```
.\test.ps1
```

You don't need to activate the virtual environment. The script runs pytest, Ruff, `dotnet test` and `dotnet format --verify-no-changes`, and stops at the first failure. Exit code 0 means all passed. If Windows blocks scripts, run `powershell -ExecutionPolicy Bypass -File .\test.ps1`.

## Files

**The app**

- `src/ExpertSelfService/`: the ASP.NET Core MVC app (`Program.cs`, `Controllers/`, `Services/`, `Views/`).
- `src/ExpertSelfService/wwwroot/`: `tokens.css` (the only file with colour codes), `app.css`, `fonts.css`, fonts and logos.
- `src/ExpertSelfService.Tests/`: xUnit tests for the app.
- `i18n/da.json`, `i18n/en.json`: all text the app shows.
- `global.json`: the .NET SDK version. `ExpertSelfService.slnx`: the solution file.

**The test data tools**

- `db/schema_sqlite.sql`: the database structure.
- `data/lookups/`: regions, cities, postcodes, fields of expertise (Fags) and roles.
- `seed.py`, `check_data.py`: build and check the test database.
- `rules.py`: the data rules both use (statuses, demo emails, length limits, CVR check).
- `settings.py`: reads `DATABASE_PATH` from the environment or `.env`.
- `tests/`: pytest tests for these tools.
- `tools/build_postcodes.py`: a one-off that fetched the Danish postcodes.

**Everything else**

- `test.ps1`: runs all tests and checks.
- `docs/`: specification, data dictionary, stories, decisions and the learning log.

## Credits

- [IBM Plex](https://github.com/IBM/plex) fonts: © IBM Corp., SIL Open Font License 1.1. The licence is in `src/ExpertSelfService/wwwroot/fonts/OFL.txt`. The fonts are served by the app itself, never from Google.
- Danish postcodes: [GeoNames](https://www.geonames.org/), CC BY 4.0.
