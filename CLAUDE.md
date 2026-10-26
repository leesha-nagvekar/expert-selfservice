# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working agreement

- **Always show a plan before writing code.** Wait for the plan to be approved. One feature per request.
- Tests come first: write the test, confirm it fails, then implement until it passes.
- Do only what was asked: no extra features, files or libraries. Every new library needs a reason in `docs/decisions.md`.
- `docs/guide.md` is the full project brief (user stories, phases, gates, review checklist). Read it when you need scope or acceptance criteria.

## Purpose

An MVP self-service app for VBA (Voldgiftsnævnet for Bygge- og Anlægsvirksomhed). It lets the experts (skønsmænd) in VBA's expert register (skønsmandsdatabasen) update their own details. It has three parts:

1. A **test copy of the expert database**: the same tables and columns as the real one, filled with 100 fake experts, 3 demo experts with known emails (e.g. `anna.demo@example.com`) and 2 secretariat users.
2. An **expert web app** (phone and desktop). The expert logs in with an email one-time code and updates name, address, company, email, phone and fields of expertise.
3. A **secretariat view**. Staff search experts, approve or reject pending changes, and see the per-expert change log.

User stories with "done when" criteria are in `docs/guide.md` section 4 (they will be copied to `docs/stories.md`). Must = E1–E7 and S1–S3. Should and Could stories come only after Gate 3. Out of scope: MitID, real email/SMS, the real database, case data and hosting.

## Ground rules: data and security (non-negotiable)

1. Never connect to the real skønsmandsdatabase. `schema/` holds a structure-only export plus lookup lists without personal data (competence list, postcodes), made by the developer in the database tool.
2. If any file contains a real name, email, phone number or address: stop and tell the user. They must delete it and report it to Claus the same day.
3. Never ask for or accept real personal data in prompts or files.
4. No secrets in code or Git. Passwords, keys and connection strings go in `.env`, and `.env` goes in `.gitignore`.
5. The app never sends email or SMS. One-time codes are written to the console and to an in-app "dev mailbox" page.
6. Every test email ends in `@example.com`.
7. The app runs on localhost only. Never deploy it or expose it.

Code rules that follow from these:
- Every query for expert data filters on the logged-in expert's ID **from the session**, never from a URL or form field.
- SQL uses parameters only, never string formatting.
- Validate input on the server. Users see plain error messages, never stack traces (no `debug=True`).

## Stack (default; any change needs Claus's approval and an entry in docs/decisions.md)

Python 3.12, FastAPI, Jinja2 server-rendered templates (no JavaScript framework), SQLite, plain CSS, pytest, ruff.

## Planned commands (update this section once they exist)

- `python seed.py --reset`: rebuild the SQLite test database with the same 100 experts every time (Faker `da_DK`, fixed seed). Run before every demo.
- `python check_data.py`: checks there are exactly 100 experts, no duplicate emails, all keys are valid and every email ends in `@example.com`.
- One command runs all tests (pytest). To run a single test: `pytest path/to/test_file.py::test_name`.
- `ruff format` and `ruff check` for formatting and linting.

## Data model rules

- Keep the original table and column names from `schema/` untouched. New self-service tables use the prefix `ss_` (login codes, pending changes, change log).
- Change log (S3): who, what, when, old value, new value. Entries are append-only and can never be edited or deleted.
- Approval rule (still to be confirmed at Gate 1): name, title and fields of expertise go to the secretariat as "Pending approval". Everything else is saved at once with a change-log entry.
- Domain validation:
  - Login codes are 6 digits, valid for 10 minutes. After 5 failed tries the account is locked for 15 minutes.
  - Postcodes are 4 digits; the city is looked up from the postcode list.
  - CVR numbers are 8 digits with a modulus-11 check (weights 2-7-6-5-4-3-2-1).
  - Fields of expertise come only from VBA's fixed list, never free text.
  - On an email change, the old email stays active until the code sent to the new address is confirmed.

## UI rules

- **Colours live only in `static/tokens.css`** as CSS variables (light and dark tokens from `docs/guide.md` section 5). Never write a colour code anywhere else. Orange `--accent` is for borders and fills only; orange text uses `--accent-text`.
- **All UI text lives in `i18n/en.json` and `i18n/da.json`**, never in code or templates. Format dates and numbers from the language setting.
- Fonts: IBM Plex Sans (400/500/600/700) for text and IBM Plex Mono (500/600) for VBA numbers, CVR numbers and codes. Serve them from `static/fonts/` and never load them from Google at runtime.
- Mobile first: the app must work at 360 px. Desktop forms are max 720 px wide and centred. Touch targets are at least 44 px. Never set fixed widths on buttons or labels.
- Top bar in `--topbar` with the white VBA logo on the left (the mark only on narrow screens) and the language switch on the right. The logo images are in `assets/images/`.
- Status is always shown as icon + colour + word (Saved, Pending approval, Rejected). Errors appear next to the field in plain words. Buttons say what they do ("Save address", not "OK").
- Read-only fields (VBA number, created date) look read-only and say "Set by VBA".

## Project files

- `LEARNING_LOG.md`: the developer's daily log. Don't edit it unless asked.
- `docs/decisions.md`: stack choices, new libraries, and review findings that were not fixed.
- `docs/SPEC.md`, `docs/data-dictionary.md`: the specification and schema explanations (written in phase 1).
