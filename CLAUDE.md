# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Always show me a plan before code

Always show me a plan before you write code. Wait until I approve it, and build one feature per request.

## Purpose

VBA (Voldgiftsnævnet for Bygge- og Anlægsvirksomhed) keeps a register of the experts it appoints to cases - the skønsmandsdatabase. When an expert moves, changes employer or gets a new email, the secretariat types it in by hand. This project builds an MVP where the experts update their own details.

It has three parts:

1. A test copy of the expert database - the same tables and columns as the real one, filled with 100 fake experts.
2. A web app for the expert. It works on phone and desktop. The expert logs in and updates name, address, company, email, phone and fields of expertise.
3. A small secretariat view where VBA staff see all changes and approve the ones that need approval.

The hand-over on 27 November 2026 is the running app shown live with the 100 test experts, the code in Git with automated tests, a README (install, run, reset, test) and the learning log.

The full brief (user stories, phases, gates, review checklist) is in `docs/guide.md`.

## Ground rules: data and security

You never copy or handle real expert data. Development and test at LITTAU run on fake or anonymised data only. These rules are not up for discussion, and they go into CLAUDE.md on day one:

1.  You make the copy yourself with your own access - structure only. Export tables, columns and keys without data, plus the lookup lists that hold no personal data, such as the competence list and postcodes. You never export a table with person rows, so there is nothing real to delete. Do the export in the database tool, never through Claude - Claude must never connect to the real database.

2.  If anything you receive contains a real name, email, phone number or address: stop, delete the file and tell Claus the same day.

3.  Never paste real data into Claude. Whatever you type or attach is sent to Anthropic's servers.

4.  No secrets in code or Git. Passwords, keys and connection strings go in a .env file, and .env goes in .gitignore.

5.  The app never sends email or SMS. One-time codes are written to the console and to a "dev mailbox" page inside the app.

6.  All test emails end in @example.com - a reserved domain, so mail can never reach a real person.

7.  The app runs on your machine only. It is never put on the internet, not even "just for the demo".

## Stack

Default, unless Claus agrees to something else: Python, FastAPI, Jinja2 templates (HTML from the server, no JavaScript framework), SQLite, plain CSS and pytest. Ruff is the formatter and linter, and one command runs all tests. The stack choice and the reason for it are written in `docs/decisions.md`.

## Colour rule

Put the colour tokens from `docs/guide.md` section 5 in `static/tokens.css` as CSS variables, and never write a colour code anywhere else. Orange `--accent` (#E96900) is too weak for text, so it is only for borders and fills. Orange text uses `--accent-text`, and green uses `--ok`.
