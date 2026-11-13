## Learning Log

** Decision which i took and asked claude and where i felt it deviated slightly away was when i asked it few questions which are listed below
(A) --reset must refuse to delete any path outside data/ or without a .db extension,
(B) reject PO-box and bulk-mail postcodes in check_data.py,
(C) check the 50-character limits against the schema and SPEC.

and it started to manipulate the code and started making the changes after this, in this process it created multiple files in tests and other folders which were not necessary so then i used the command /rewind and restored the context back to the stable version of the code.

This was a learning for me where i used the claude code command like /rewind.

## 2 Nov
2 Nov: The stack changed from Python to ASP.NET Core. I saved the Python skeleton under a branch and tag instead of using /rewind, started a new branch from the last docs commit, and reused i18n, tokens and fonts. Claude in VS Code kept assuming Python until CLAUDE.md was updated - lesson: update CLAUDE.md first when requirements change.

## 3 Nov
After the stack change i went through the db file where the table for expert CV was missing so added it from the old schema. Also checked the whole base that we have built untill now against the new guide and found several issues, which have been planned and reviewed and fixed. The .NET 10 SDK (10.0.403) is installed and CLAUDE.md was updated with all details. 
There is one issue from the list which needs to be addressed 
Education (E8): VBA's schema has no column for it. To decide with Claus before Gate 3: where VBA keeps it today, one field or several, max length, and whether it needs approval. Proposal: side table ss_expert_education (candidate_id PK, education TEXT, max 250), saved at once.

## 4 Nov 2026

Built the ASP.NET Core skeleton (phase 3) with Claude, test first at every step: clean .venv, solution and test project, /static files moved to wwwroot/, top bar and front page, language switch, own 404/500 pages on 127.0.0.1 only, and test.ps1 running all tests in one command.

**Learned:** Read the failing tests before saying "go" - that's where wrong assumptions show up. 

## 9 Nov 2026 – E1 login

**Asked:** Planned E1 in Plan mode and answered Claude's four decisions (cookie login instead of a session secret in .env, DotNetEnv to read .env, log out and landing pages, Help left open for Claus). Asked Claude to explain the diff as a colleague's PR, and clicked through in the browser (Anna, Sofie, Bo locked, Retired email, both languages, 360 px, dark mode).

**Went wrong:**
- When I broke the code on purpose, removing the purpose filter from the login query didn't make any test fail. The hash still blocked the wrong code, so the test never reached the filter. A newer email-change code could have hidden a valid login code without any test noticing. I added the missing test, saw it fail, put the filter back, and now all tests pass.
- Claude's review raised five small issues. I fixed two : the "10 minutes" written in two places and spaces inside the code. Other three recorded as accepted in decisions.md (anyone can lock anyone out, code requests have no limit, and the console shows the email and code.).

**Learned:**
- How a cookie login works, and why logging out can't cancel a copied cookie.
- Why unknown emails must lock too: otherwise the lock reveals who is in the register.
- Why the format check comes before CheckCode: a typo like "12ab56" shouldn't use one of the 5 tries.

## 10 Nov 2026 – E2 profile, central login check, email format

**Asked:**
- Planned E2 (profile, X4) in Plan mode. The page shows the expert's own details in read-only sections, "Not given" for empty fields, a "Set by VBA" box, status as icon + colour + word, British dates in English.
- Asked Claude to explain the diff as a colleague's PR, and answered its questions: 
1. Empty middle name stays hidden
2. OldStatus left out
3. An info icon for an unknown status.
4. Moved the "Retired or removed" check into the login cookie (OnValidatePrincipal), so every expert page is protected, including the ones E3–E7 will add.

**Went wrong:**
- In the click-through I typed "bo.demo@example.com)" and went straight to the code page, with no code sent. All tests passed, because they followed Claude's plan, which had decided "no format check". I asked for an email format check.

**Learned:**
- Breaking the code on purpose proved the tests catch it.
- The browser checks finds errors and issues sometimes tests can't.

## 11 Nov 2026 – E3 address and phone (T4)

**Asked:** Built E3 in one session. The expert can edit street, postcode and three phones; typing a 4-digit postcode fills in the city by itself (with "Find city" when JavaScript is off). Every change is saved at once, with LastUpdated and one change-log row per changed field, in one transaction. Reviewed the diff, did three break-it checks, and clicked through in both languages, at 360 px and in dark mode.

**Went wrong:**
- Removing Commit() turned most saving tests red, but one stayed green: it only checked the redirect after saving, not that the phone was really stored. I asked for the missing check.
- The Danish texts didn't match ("Gade" vs "Vej", "Ret" vs "Rediger"). I made them consistent.

**Learned:**
- A passing test can still miss the point. Breaking the code on purpose found a weak test for the second time (after E1's purpose filter).

## 12 Nov – E4 Change email

- **Asked:** Claude to build E4: a code sent to the new address only, the old email working until the code is confirmed, and the "already in use" check only after the right code. Then break-it checks and a manual click-through.

- **Went wrong:** Break-it checks caught issues, (test The_right_code_clears_the_count passed) and the tests were fixed. 

- **Learned:** a passing test isn't proof, so break the code to see if the test notices. 
  
  ## 12 Nov – E5 Change name

- **Asked:** Claude to build E5: a name change goes to the secretariat for approval, one pending row per changed field Candidates untouched until S2, and a pending field can't be changed again (the server refuses it too). The job title moved to E6, as SPEC says.

- **Went wrong:** Claude's file tool wrote invisible Unicode line breaks into the test file, and VS Code warned me. They're now written as escapes (\u2028). The error page showed 404 instead of 400 for a form with a NUL character: the E4 fix ([FromRoute]) wasn't enough, so the page now reads the status only from the address. In the click-through, the
"Pending approval" box looked too big, so I made it smaller.

- **Learned:** a fix for one case can miss a related case, so a test for each case is what finds it. 

## 12 Nov – E6 Company and job title

- **Asked:** Claude to build E6: company, CVR and job title, saved at once, with roles shown as "Set by VBA". check_data py flagged a shared CVR as an error, so I decided that experts from the same company may share one CVR (CVR is the
company's number, not the expert's; RegistrationId is the expert's own VBA number).
- 
**Went wrong:** 00000000 passed the modulus-11 check (the sum is 0), so I added "can't start with 0". Python's \d also accepts non-ASCII digits, so the rule is now [1-9][0-9]{7}. Claude's own test data had an invalid CVR (23456781); the code correctly refused it, and the test was fixed. 

## 12–13 Nov – E7 Fields of expertise

- **Asked:** 
Claude to build E7: tick boxes from VBA's fixed list, at least one field, sent for approval as one pending change with the whole old and new list in Danish order. The page is locked while a change is pending.

- **Went wrong:** removing the pending check in SkillService.Send failed no test. Every test stopped at the controller first, so the service was never tested. Claude added a test that calls the service directly directly while a change is pending. 

- **Learned:** when there are similar checks at two places then we must do the write the tests for both places checks. 