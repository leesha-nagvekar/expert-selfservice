# Specification: Expert Register Self-Service (MVP)

Status: draft for Gate 1 (28 Oct 2026). Stories: `docs/stories.md`. Tables and columns: `docs/data-dictionary.md`.

## 1. Decisions from the interview (27 Oct 2026)

| # | Decision |
|---|---|
| 1 | Fields of expertise are VBA's fixed list in **Fags**. An expert's fields are rows in **CandidateSkills**, where `Skill` holds the Fag name as text. **ProfessionTags** (via CandidateTags) are roles, not fields of expertise. VBA maintains the Fags list (add, rename, delete). The expert only picks from it. |
| 2 | Changes waiting for approval go in the new table **ss_pending_changes**. The app never writes to the `_Temp` tables. |
| 3 | The change log is the new append-only table **ss_change_log**. `CandidateChangeLog` is left untouched. |
| 4 | Title = `Candidates.Designation`. Role = the expert's ProfessionTags (via CandidateTags). |
| 5 | Address = `Street` + `ZipcodeId`. `Address` is neither shown nor changed. |
| 6 | All three phones (`PhoneMobile`, `PhoneWork`, `PhoneHome`) can be edited. All are optional. |
| 7 | VBA number = `RegistrationId`. Read-only. |
| 8 | The SQLite test database holds only the tables the app uses: Candidates, Zipcodes, Cities, Regions, Fags, CandidateSkills, ProfessionTags, CandidateTags, plus the ss_ tables. Names and columns stay as in `schema/`. |
| 9 | Every status except Retired (0) can log in. Status is read-only in the app. |
| 10 | Approval rule: the Field Rule in section 3. It differs from the guide: Designation (job title) is saved at once, and roles are set by VBA only. |

Items marked **(proposal)** were not part of the interview. Check them before Gate 1.

## 2. Users and access

- **Expert**: a row in Candidates with `Status <> 0`. Logs in with the email in `Candidates.Email`.
- **Secretariat user**: a row in `ss_staff_users`. Logs in the same way with their own email.
- After login the session holds either `expert_id` or `staff_id`, never both. Expert pages refuse staff sessions and the other way round.
- Every query for expert data on expert pages uses `expert_id` **from the session**. No expert page takes an expert id from the URL or a form. Changing an id in the URL gives "Page not found" (demo step 7).
- The session secret lives in `.env`.

## 3. Approval rule (Field Rule)

| Field | Rule |
|---|---|
| Street, postcode, the three phone numbers, company, CVR, Designation (job title) | The expert edits. Saved at once, with an entry in `ss_change_log`. |
| First name, last name | The expert edits. Goes to the secretariat for approval. |
| Email | Confirmed with a code sent to the new address (E4). |
| Fields of expertise (Fags / CandidateSkills) | The expert adds or removes, from the Fags list only - no free text, no new values. Goes to approval. The list itself is maintained by VBA. |
| ProfessionTags / CandidateTags (Skønsmand, Sagkyndig, Mediator, Enedommer) | Read-only, marked "Set by VBA". VBA appoints, the expert does not. |
| RegistrationId, Status, UntilDate, OldStatus | Read-only, marked "Set by VBA". |

- MiddleName is not in the Field Rule. It is treated as part of the name and goes to approval (open point F).
- Created (date added) is read-only, marked "Set by VBA" (story E2).

- Saved at once means the value in Candidates is updated, `LastUpdated` is set to today, and one ss_change_log row per changed field is written in the same transaction.
- Pending approval means one ss_pending_changes row is written. Candidates is not touched until the secretariat approves.
- **(proposal)** While a change to a field is pending, the expert can't edit that field again. The screen shows the pending value and "Pending approval".
- On approval the new value is written to Candidates (or CandidateSkills), `LastUpdated` is set, and a change-log row is written. Who = the secretariat user, plus a link to the pending change, which shows which expert asked for it.
- On rejection nothing in Candidates changes. The expert sees "Rejected" and the reason on the profile until they make a new change to that field.

## 4. Screens

Every screen has the top bar: the white VBA logo on the left (the mark on narrow screens) and the language switch (Dansk / English) on the right. Help and "Contact the secretariat" are reachable from every screen. Status is always shown as icon + colour + word. Errors appear next to the field.

### Expert

**X1 Login: email (E1)**
- Field: email.
- "Send code" always shows the same confirmation, "If the email is registered, a code has been sent". A code is only created when the email belongs to an expert with Status ≠ 0, or to a secretariat user.
- If the email is locked: "Too many attempts. Try again in N minutes."

**X2 Login: code (E1)**
- Field: 6-digit code.
- Valid for 10 minutes and can only be used once. A new code makes older unused codes invalid.
- Each wrong code counts as a failed try for that email. After 5 failed tries the email is locked for 15 minutes. A correct code resets the count.
- Errors: "The code must be 6 digits", "The code is wrong or has expired".

**X3 Dev mailbox (E1, E4)**
- Lists every message "sent" by the app, newest first: to, subject, text (with the code), time.
- Every code is also printed to the console.
- Only exists because the app runs on localhost. It doesn't need a login.

**X4 Profile overview (E2)**
- Sections, each with a button that opens its edit screen:
  - Name
  - Address and phone
  - Email
  - Company and job title
  - Fields of expertise
- Read-only, marked "Set by VBA": VBA number (Plex Mono), created date, status (shown as an English or Danish word, with "until <date>" when Standby), roles.
- Each section shows its state: nothing, "Saved" (just after saving), "Pending approval" or "Rejected" + reason.
- Never shown: Notes, Ratings, Address, Fax, Website, name history columns.

**X5 Edit address and phone (E3)**
| Field | Column | Rule |
|---|---|---|
| Street and number | Street | Required, max 50 characters. |
| Postcode | ZipcodeId (via Zipcodes) | Exactly 4 digits and must exist in Zipcodes. Otherwise: "Postcode must be 4 digits" / "Unknown postcode". |
| City | via Zipcodes → Cities | Not typed. Filled in from the postcode when the page reloads after the expert presses "Find city" or "Save address". **(proposal)** |
| Mobile, work, home phone | PhoneMobile, PhoneWork, PhoneHome | Optional. **(proposal)** Digits, spaces and a leading +, 8–15 digits, max 50 characters. |
- Button: "Save address and phone". Saved at once.

**X6 Change email (E4)**
- Field: new email. Max 50 characters, a valid email format, not used by another expert or a staff user. In test data it must end in @example.com.
- "Send code to new email" sends a 6-digit code to the **new** address (dev mailbox). Same rules as login: 10 minutes, 5 tries, 15-minute lock.
- The old email keeps working for login until the code is confirmed. Then Email is updated and a change-log row is written.
- **(proposal)** A new request cancels an older unconfirmed one.

**X7 Edit name (E5)**
| Field | Column | Rule |
|---|---|---|
| First name | FirstName | Required, max 50. |
| Middle name | MiddleName | Optional, max 50. |
| Last name | LastName | Required, max 50. |
- Button: "Send name for approval". One pending change per changed field.

**X8 Edit company and job title (E5 title, E6)**
| Field | Column | Rule |
|---|---|---|
| Company name | Company | Optional, max 250. |
| CVR number | ss_expert_company.cvr | Optional. If filled in: exactly 8 digits, and the sum of the digits times 2-7-6-5-4-3-2-1 must be divisible by 11. Error: "CVR number must be 8 digits" / "This is not a valid CVR number". |
| Job title | Designation | Optional, max 250. |
- Roles are shown read-only here, marked "Set by VBA".
- Button: "Save company and job title". Saved at once.
- The job title sits here, not with the name, because it is saved at once and the name goes to approval. One screen never mixes the two (proposal).

**X9 Edit fields of expertise (E7)**
- A list of tick boxes with every Fags.Fag. No free text.
- The server accepts only names that exist in Fags.
- The expert can't create, rename or delete Fags. VBA maintains the list, and the app has no screen for that (no story asks for one).
- **(proposal)** At least one field must be chosen.
- Button: "Send for approval". One pending change with the whole old list and the whole new list (names sorted and separated by "; ").
- On approval the expert's CandidateSkills rows are replaced by the new list.

**X10 About your data**
- In plain words: what is stored about the expert, who can see it (the expert and VBA's secretariat), what needs approval, and how to contact the secretariat.

### Secretariat

**S-A Search experts (S1)**
- Filters, combined with AND, all optional:
  - name: matches part of FirstName, MiddleName or LastName, case-insensitive
  - email: matches part of the email
  - postcode: exact 4 digits
  - field of expertise: chosen from Fags
- Results: name, VBA number, postcode + city, status, number of pending changes. Each result opens the expert page.

**S-B Pending changes (S2)**
- A list of all pending changes, oldest first: expert, field, old value, new value, date.
- "Approve" applies the change (section 3).
- "Reject" requires a reason, max 500 characters (proposal). Error: "Write a reason for rejecting".
- A change that has already been decided can't be decided again.

**S-C Expert page and change log (S3)**
- The expert's details (read-only) and their change log, newest first: when, who (expert or secretariat user), field, old value, new value.
- There is no edit or delete anywhere. The database blocks it too (section 5).

## 5. New tables (prefix ss_)

All times are stored in UTC and shown in local time (Europe/Copenhagen), formatted from the chosen language.

**ss_staff_users**: secretariat users.
`id` PK · `name` · `email` UNIQUE (ends in @example.com) · `created_at`

**ss_login_codes**: one-time codes for login and email change.
`id` PK · `email` · `purpose` (`login` / `email_change`) · `code_hash` (the code is never stored in plain text) · `created_at` · `expires_at` (created + 10 min) · `used_at` (empty until used)

**ss_login_lockouts**: failed tries per email.
`email` PK · `failed_count` · `locked_until` (empty or a time)

**ss_dev_mailbox**: messages the app "sends".
`id` PK · `to_email` · `subject` · `body` · `created_at`

**ss_email_changes**: email changes waiting for confirmation.
`id` PK · `candidate_id` → Candidates.Id · `new_email` · `created_at` · `confirmed_at` · `cancelled_at`

**ss_expert_company**: data the original schema has no column for.
`candidate_id` PK → Candidates.Id · `cvr` (8 digits or empty)

**ss_pending_changes**: changes waiting for approval.
`id` PK · `candidate_id` → Candidates.Id · `field` (`FirstName`, `MiddleName`, `LastName`, `Skills`) · `old_value` · `new_value` · `status` (`pending` / `approved` / `rejected`) · `reason` (required when rejected) · `created_at` · `decided_by` → ss_staff_users.id · `decided_at`

**ss_change_log**: append-only change log.
`id` PK · `candidate_id` → Candidates.Id · `actor_type` (`expert` / `staff`) · `actor_id` · `field` · `old_value` · `new_value` · `pending_change_id` → ss_pending_changes.id (empty for changes saved at once) · `created_at`
SQLite triggers stop every UPDATE and DELETE on this table with an error.

**ss_languages**: the languages an expert can choose from (for E8).
`id` PK · `name` UNIQUE (Dansk, Engelsk, Tysk, Svensk, Norsk, Fransk)

**ss_expert_languages**: the languages of each expert.
`candidate_id` → Candidates.Id · `language_id` → ss_languages.id · PK (both)

The SQLite structure of all tables is in `db/schema_sqlite.sql`.

## 6. Story → tables and columns

| Story | Reads | Writes |
|---|---|---|
| E1 login | Candidates.Email, Status; ss_staff_users | ss_login_codes, ss_login_lockouts, ss_dev_mailbox |
| E2 profile | Candidates (FirstName, MiddleName, LastName, Designation, RegistrationId, Created, Status, UntilDate, Street, ZipcodeId, phones, Email, Company); Zipcodes→Cities; CandidateSkills; CandidateTags→ProfessionTags; ss_expert_company; ss_pending_changes | – |
| E3 address, phone | Zipcodes, Cities | Candidates.Street, ZipcodeId, PhoneMobile, PhoneWork, PhoneHome, LastUpdated; ss_change_log |
| E4 email | Candidates.Email | ss_email_changes, ss_login_codes, ss_login_lockouts, ss_dev_mailbox; Candidates.Email; ss_change_log |
| E5 name | Candidates.FirstName, MiddleName, LastName | ss_pending_changes |
| E5 title, E6 company | Candidates.Company, Designation; ss_expert_company; CandidateTags→ProfessionTags (read-only) | Candidates.Company, Designation, LastUpdated; ss_expert_company; ss_change_log |
| E7 fields of expertise | Fags; CandidateSkills | ss_pending_changes |
| S1 search | Candidates; Zipcodes→Cities; CandidateSkills; Fags; ss_pending_changes | – |
| S2 approve / reject | ss_pending_changes | ss_pending_changes; Candidates or CandidateSkills; ss_change_log |
| S3 change log | ss_change_log; ss_staff_users; Candidates | – |

Should and Could stories (E8–E11, S4) are specified after Gate 3.

## 7. Test data (`python seed.py --reset`)

- The same 100 fake experts every time (Faker `da_DK` 40.44.0, seed 2026, reference date 2026-10-01): 97 random + 3 demo experts (Id 1–3) = exactly 100. Plus 2 secretariat users. Every email ends in @example.com.
- Demo logins: anna.demo@example.com (Active, postcode 2100), bo.demo@example.com (Standby, no phone, no company), carl.demo@example.com (Accepted, 4 fields, 2 roles). Secretariat: sofie.staff@example.com, mads.staff@example.com.
- Real Danish postcodes and cities. CVR numbers that pass the modulus-11 check. 1–4 fields of expertise per expert, taken from Fags. 1–2 roles each. Exactly 10 experts with no phone number. Statuses: Active 53, Accepted 11, Under observation 8, Evaluated 10, Standby 11, Retired 7. About 30 % speak a second language.
- Lookup lists live in `data/lookups/` (in Git):
  - Regions, Fags: copied from VBA's lookup export (28 Oct 2026). Fags has the 10 exported names only.
  - ProfessionTags: the 9 exported roles plus 3 made-up roles (Voldgiftsdommer, Bisidder, Tilsynsførende, Ids 43–45).
  - Cities, Zipcodes: the exported Swedish/Norwegian rows plus all 1,089 normal Danish postcodes from GeoNames (CC BY 4.0), Ids from 2001. Built once with `tools/build_postcodes.py`. PO-box and bulk-mail postcodes are left out.
  - The export files were deleted from `schema/` after use.
- RegistrationId: made-up whole numbers from 900001 up, digits only (open point B).
- `python check_data.py` checks the rules above. `seed.py --reset` runs it at the end.
- `schema/schema.sql` is UTF-16. Any script that reads it must open it as UTF-16.

## 8. Open points for Gate 1

- **A. Roles:** settled. Set by VBA only, read-only for the expert (Field Rule, section 3).
- **B. VBA number:** settled. RegistrationId is a whole number with 1 or more digits, stored as text. The test data uses made-up numbers in the same pattern, never real ones.
- **C. Status names:** settled. Standby (-1), Retired (0), Active (1), Accepted (2), Under observation (3), Evaluated (4).
- **D. Lookup lists:** settled for test data (section 7). The exports were samples, so Fags has 10 names and ProfessionTags has 3 made-up additions.
- **E. Proposals:** the items marked (proposal) above.
- **F. Middle name:** not in the Field Rule. For now it goes to approval with first and last name.
- **G. Fags list:** settled. VBA maintains Fags. The expert only picks from it.

