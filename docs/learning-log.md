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

**Asked:** Built E3 in one session. The expert can edit street, postcode and three phones; typing a 4-digit postcode fills in the city by itself (with "Find city" when JavaScript is off). Every change is saved at once, with LastUpdated and one change-log row per changed field, in one transaction. Did three break-it checks, and clicked through in both languages, at 360 px and in dark mode.

**Went wrong:**
- Removing Commit() turned most saving tests red, but one stayed green: it only checked the redirect after saving, not that the phone was really stored. Claude wrote missing check.
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

- **Asked:** Claude to build E6: company, CVR and job title, saved at once, with roles shown as "Set by VBA". check_data py flagged a shared CVR as an error, so Claude decided that experts from the same company may share one CVR (CVR is the
company's number, not the expert's; RegistrationId is the expert's own VBA number).
- 
**Went wrong:** 00000000 passed the modulus-11 check (the sum is 0), so I added "can't start with 0". Python's \d also accepts non-ASCII digits, so the rule is now [1-9][0-9]{7}. Claude's own test data had an invalid CVR (23456781); the code correctly refused it, and the test was fixed. 

## 12–13 Nov – E7 Fields of expertise

- **Asked:** 
Claude to build E7: tick boxes from VBA's fixed list, at least one field, sent for approval as one pending change with the whole old and new list in Danish order. The page is locked while a change is pending.

- **Went wrong:** removing the pending check in SkillService.Send failed no test. Every test stopped at the controller first, so the service was never tested. Claude added a test that calls the service directly directly while a change is pending. 

- **Learned:** when there are similar checks at two places then we must do the write the tests for both places checks. 

## 16–17 Nov – S1 Search experts

- **Asked:** 
Claude to build S1: the secretariat searches by name, email, postcode and field of expertise. I made few changes in the plan: one search box (every word must match, so "anna 2100" works), tick-box filters for Region (region replaced the postcode range), Rolletype, Status (OR within a group, AND between groups). Layout: each filter group folds open and closed with a count badge, long groups get a find box and an 8-row scroll box, results show in table 25 per page with pagination.

- **Went wrong:** 
Claude's review found the code didn't follow the plan (two database connections instead of one read transaction), claude fixed it.
The view according to plan didnt have the filters with group folds and no table, it listed the whole list on one page which made it too long and improper. I fixed the view with adding the table with 25 per page and side filters folds. Also added the 'clear filter' at the top instead of at bottom.

- **Learned:** 
The layout choices can be made according to the view and how data loads and these go in decisions.md.

## 18–19 Nov – S2 Approve or reject

- **Asked:** 
Claude to build S2: a "Pending approvals" page where the secretariat approves or rejects changes, with a search box and a reason required for rejecting. The expert then sees "Approved" or "Rejected" with the reason. Fields of expertise show as Added / Removed.

- **Went wrong:** Claude's self-review found that approving skills used the current list instead of what Sofie saw, and that the "decide once" check didn't check its own result. Both were fixed, with tests.
In my break-it checks, I removed a check in PendingController (the one that says "already decided" when Sofie rejects with an empty reason on a change that was already decided). No test failed, so Claude added two tests for that.

## 19–20 Nov – S3 step A: expert page and change log (S-C)
- **Asked:** 
Claude to plan S3 in three steps and build step A first: the expert page with read-only details, pending changes and the change log (newest first, 10 per page).

- **Went wrong:** 
In my break-it checks, removing the expert filter from the skills query showed one experts skill on anothers page and no test failed, so Claude added two tests.

## 20 Nov – S3 step B: approve or reject on the expert page (S-C)
- **Asked:** 
Claude to add Approve and Reject to the pending changes on S-C. One rule: a change can only be decided from the page of the expert it belongs to.

- **Learned:** Breaking a check in two directions proves the tests cover two sides.

## 20 Nov – S3 step C: back link on the expert page (S-C)

- **Asked:** Claude to add a " Back to search" / " Back to pending approvals" link on S-C that returns to the list as Sofie left it, with a safety rule so the link can only go to /staff or /staff/pending.

- **Went wrong:** 
Pager break-it showed that PageUrl trusts back without checking; it does no harm, because S-C checks back again on the next request.

## 23 Nov – Phase 6: /code-review, fix #1 (back link to a page that no longer exists)

- **Asked:** 
Claude to run /code-review and it found that "Back to search" could open an error page: after approving on S-C, a "pending only" search gets shorter, and the back link still pointed to a page that no longer existed. Claude added tests and fixed the problem.

- **Learned:**
A code review can find problems where two features meet, even when each feature's own tests pass

## 23 Nov – Review fix #2: line breaks in a reject reason

- **Asked:** 
Claude to fix /code-review finding #2: a line break in a reject reason was counted as two characters, so a reason that fit the box (500) was refused as too long. Claude added tests and fixed the problem now each line break becomes one.

- **Went wrong:** 
Click-through found a problem: the expert saw 5-line reason as one paragraph. I asked Claude to fix it, so the reason is now shown with its line breaks on the profile.

- **Learned:** 
A test can prove the data is right while the screen still looks wrong, only a click-through shows what the user sees.

## 23 Nov – Review fix #4: one order for fields and roles on S-C

- **Asked:** 
/code-review found that on S-C the fields of expertise were sorted by the page language, so Danish and English showed different orders (Æ counts as "ae" in English). Claude fixed it: S-C now always uses the Danish order.

- **Went wrong:** I couldn't see the bug in the app at first, because the seeded fields start with normal letters, so both languages looked the same. For the click-through, asked Claude to add two test fields (Afløb, Æstetik) to Anna in my local database only, which I removed afterwards.

## 23 Nov – Review fix #3: an approval that changes nothing

- **Asked:** 
/code-review found that if a change was already in place when it was approved, a log row with old = new was written and LastUpdated moved. Claude fixed it: the change is still marked approved and "Approved" is shown, but no log row is written.

## Phase 6 – Review finding #5 (top bar "current page" mark)

**Asked:** 
Claude to fix finding #5. The top bar didn't mark the page I was on when the address had capital letters or a slash at the end (/Staff/, /staff/pending/). Also added a guard test so the expert page marks neither link.

### Phase 6 – /security-review (phase5-secretariat)

**Asked:** 
Claude to run /security-review on the phase5-secretariat branch before merging. Then I asked whether anything was found below its 8/10 confidence bar. No vulnerabilities found.