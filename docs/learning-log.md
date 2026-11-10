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
