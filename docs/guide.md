# Expert Register Self-Service – Project Guide

16 Oct 2026 · @Someone

Build a working MVP where VBA's experts update their own details, using a test copy of the expert database with 100 fake people. You do the steering, Claude does most of the typing. Claus follows along through a status report every Friday and five tests on the way. Follow the phases in order and keep the deadlines - if you want to do something differently, ask Claus first. You present the result on 27 Nov 2026.

## 1. The assignment

VBA (Voldgiftsnævnet for Bygge- og Anlægsvirksomhed) keeps a register of the experts it appoints to cases - the skønsmandsdatabase. When an expert moves, changes employer or gets a new email, the secretariat types it in by hand. You build the app that lets the expert do it themselves.

You build three things:

1.  A test copy of the expert database - the same tables and columns as the real one, filled with 100 fake experts.

2.  A web app for the expert. It works on phone and desktop. The expert logs in and updates name, address, company, email, phone and fields of expertise.

3.  A small secretariat view where VBA staff see all changes and approve the ones that need approval.

On 27 November you hand over:

- The running app, shown live with the 100 test experts.

- The code in a Git repository, with automated tests.

- A README: how to install, run, reset and test.

- Your learning log: what you asked Claude, where it was wrong, what you changed.

This is a learning project. The goal is that you can lead a build with Claude from an empty folder to a working MVP and defend every line of it. The subject is real, so the constraints are real too.

## 2. Your role: you steer, Claude builds

You are the lead developer and Claude is your team. Claude is fast and knows a lot. It also makes mistakes with full confidence, and it knows nothing about VBA unless you tell it. Your job is to decide, check and correct.

Seven rules:

1.  You decide what gets built. Claude proposes, you choose.

2.  Plan before code. Switch to Plan mode (Shift+Tab in the terminal, the mode picker in the desktop app), read the plan, correct it, then let Claude build.

3.  Small steps. One feature per request. Commit to Git every time something works - then a bad step costs minutes, not days.

4.  Never accept code you can't explain. Ask Claude to explain it line by line, or to make it simpler.

5.  Tests come with every feature. Ask for the test first, watch it fail, then let Claude build until it passes.

6.  The project rules live in CLAUDE.md in the repo root. Claude reads it at the start of every session. When Claude makes the same mistake twice, add a rule there.

7.  Keep LEARNING_LOG.md - three lines a day: what you asked, what went wrong, what you learned. It becomes half your presentation.

A good request has four parts: context, task, constraints and "done when". Example:

Context: Self-service app for VBA's expert register. Rules and stack are in CLAUDE.md.

Task: Add the "Edit contact details" page for the logged-in expert.

Constraints: Only the expert's own record. Colours from static/tokens.css.

All UI text in i18n/en.json and i18n/da.json.

Done when: the expert can change address and phone, the change is saved

with a change-log entry, and a test proves it.

Show me the plan first. Do not write code yet.

Commands you will use daily: /init (creates CLAUDE.md), Plan mode, /code-review, /security-review, /clear (fresh start when a session goes in circles), /rewind (undo Claude's last steps), /export (save the session to a file for the tests) and Esc (stop Claude mid-step). Ask Claude itself when you are unsure how a feature works - "how do I ..." questions about Claude Code are fine.

## 3. Ground rules: data and security

You never copy or handle real expert data. Development and test at LITTAU run on fake or anonymised data only. These rules are not up for discussion, and they go into CLAUDE.md on day one:

1.  You make the copy yourself with your own access - structure only. Export tables, columns and keys without data, plus the lookup lists that hold no personal data, such as the competence list and postcodes. You never export a table with person rows, so there is nothing real to delete. Do the export in the database tool, never through Claude - Claude must never connect to the real database.

2.  If anything you receive contains a real name, email, phone number or address: stop, delete the file and tell Claus the same day.

3.  Never paste real data into Claude. Whatever you type or attach is sent to Anthropic's servers.

4.  No secrets in code or Git. Passwords, keys and connection strings go in a .env file, and .env goes in .gitignore.

5.  The app never sends email or SMS. One-time codes are written to the console and to a "dev mailbox" page inside the app.

6.  All test emails end in @example.com - a reserved domain, so mail can never reach a real person.

7.  The app runs on your machine only. It is never put on the internet, not even "just for the demo".

## 4. What the app must do

The MVP is the 10 Must stories below. Should and Could come only after Gate 3 on 13 November. There are two kinds of user: the expert (skønsmand) and VBA's secretariat. Create two secretariat test users next to the 100 experts.

| **ID** | **User story**                                          | **Done when**                                                                                                             | **Priority** |
|--------|---------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------|--------------|
| E1     | As an expert I log in with my email and a one-time code | 6-digit code, valid 10 minutes, max 5 tries, then locked for 15 minutes. The code appears in the dev mailbox              | Must        |
| E2     | As an expert I see my own profile                       | All my fields are shown. VBA number and created date are read-only and marked "Set by VBA"                                | Must        |
| E3     | As an expert I update my address and phone              | A 4-digit postcode fills in the city from the postcode list. Saved at once, with old and new value in the change log      | Must        |
| E4     | As an expert I change my email                          | A code goes to the new address. The old email stays active until the code is confirmed                                    | Must        |
| E5     | As an expert I change my name or title                  | The change goes to the secretariat. I see "Pending approval" until it is decided                                          | Must        |
| E6     | As an expert I update my company                        | Company name, CVR number (8 digits, modulus-11 check) and my role. Saved at once                                          | Must        |
| E7     | As an expert I change my fields of expertise            | Picked from VBA's fixed list, no free text. Goes to approval - the secretariat searches on these when it appoints experts | Must        |
| E8     | As an expert I update languages and education           | Languages from a list, education as short text                                                                            | Should      |
| E9     | As an expert I use the app in Danish or English         | All text comes from i18n/da.json and i18n/en.json, none is written in the code                                            | Should      |
| E10    | As an expert I upload my CV                             | PDF only, max 5 MB, replaces the old one                                                                                  | Could       |
| E11    | As an expert I get a dark theme                         | Follows the phone's setting, using the dark tokens in section 5                                                           | Could       |
| S1     | As the secretariat I search experts                     | By name, email, postcode and field of expertise                                                                           | Must        |
| S2     | As the secretariat I approve or reject a pending change | Rejecting requires a reason. The expert sees the outcome and the reason                                                   | Must        |
| S3     | As the secretariat I see the change log per expert      | Who, what, when, old value, new value. Nobody can edit or delete an entry                                                 | Must        |
| S4     | As the secretariat I export changes since a date        | CSV file, one row per change                                                                                              | Could       |

Out of scope: MitID login, real email or SMS, any connection to the real database, case data (cases, fees, documents) and hosting.

The MVP is done when:

- [ ] All 10 Must stories work in the demo.

- [ ] All tests run with one command and pass.

- [ ] /code-review and /security-review have run, and every finding is fixed or explained in docs/decisions.md.

- [ ] Someone else installs and starts the app from the README in under 15 minutes.

- [ ] The repository holds no real data and no secrets.

- [ ] It follows section 5 at 360 px phone width and on desktop.

## 5. VBA look and feel

The app must look like VBA. The brand blue \#00437C comes from VBA's logo, and the palette below is the one in the VBA App design proposal. Put the tokens in static/tokens.css as CSS variables and never write a colour code anywhere else. Claude can write the file straight from this table.

| **Token**     | **Light** | **Dark** | **Use**                                                                       |
|---------------|-----------|----------|-------------------------------------------------------------------------------|
| --brand       | \#00437C  | \#3D7FBF | Primary buttons, links, focus ring                                            |
| --topbar      | \#00437C  | \#00437C | Top bar - logo blue in both themes, always with white text and the white logo |
| --secondary   | \#1F5F8B  | \#7FB0DC | Sub-headings, selected states, text in info boxes                             |
| --ink         | \#1E2B39  | \#EDF3F9 | Headings                                                                      |
| --text        | \#34495E  | \#D6DEE6 | Body text                                                                     |
| --muted       | \#6C7C8D  | \#8A99A8 | Help text under fields                                                        |
| --accent      | \#E96900  | \#F0952F | Borders and fills of "Pending approval" badges - never small text             |
| --accent-text | \#B05407  | \#F0952F | Orange text, e.g. the words "Pending approval"                                |
| --ok          | \#0E7C55  | \#4FBF95 | "Saved" and "Approved"                                                        |
| --surface     | \#F4F6F9  | \#1E2833 | Cards, form fields                                                            |
| --background  | \#FFFFFF  | \#151C24 | Page                                                                          |
| --line        | \#E7ECF1  | \#2A3644 | Borders, dividers                                                             |
| --brand-soft  | \#E8EFF6  | \#17293A | Info boxes                                                                    |
| --accent-soft | \#FCF0E4  | \#33251A | Warning boxes                                                                 |
| --on-brand    | \#FFFFFF  | \#0B131B | Text on --brand buttons                                                       |

Orange \#E96900 on white has a contrast of about 3:1, too weak for text, so it is only for borders and fills. Orange text uses --accent-text and green uses --ok - both about 5:1. Blue on white is about 10:1 and safe everywhere.

Type: [IBM Plex Sans](https://fonts.google.com/specimen/IBM+Plex+Sans) for all text and [IBM Plex Mono](https://fonts.google.com/specimen/IBM+Plex+Mono) for VBA numbers, CVR numbers and one-time codes - the same fonts as the VBA App design. Both are free under the SIL Open Font License. Use Sans in 400, 500, 600 and 700, and Mono in 500 and 600, with system-ui, sans-serif as fallback. Download the font files and serve them from the app itself (static/fonts/) - loading them from Google at runtime sends every user's IP address to Google. Body 16 px, help text 14 px, page title 24 px, line height 1.5.

Layout rules from the design proposal:

- Mobile first. It must work from 360 px wide. On desktop, forms are max 720 px wide and centred.

- Top bar in --topbar with the VBA logo left and the language switch right. Use vn-logo-white.png, and vn-mark-white.png when the screen is too narrow for the full logo.

- Touch targets at least 10 mm (44 px).

- No icon without text. A button says what it does: "Save address", not "OK".

- One task per screen: profile overview, tap a section, edit, save, back to the overview with a confirmation.

- Read-only fields look read-only and say who sets them.

- Status is always icon + colour + word: Saved, Pending approval, Rejected. Never colour alone.

- Errors sit next to the field in plain words: "Postcode must be 4 digits".

- Help and "Contact the secretariat" are at most two taps away from any screen.

- No fixed widths on buttons or labels. Text may wrap - German and Finnish run 25-35 % longer than Danish.

- Dates and numbers are formatted from the language setting, never typed as text.

- An "About your data" page says in plain words what is stored and who sees it.

VBA's logo files are below - right-click an image to save it. The blue versions go on white, the white versions on the blue top bar.

![](media/image2.png)

![](media/image3.png)

The white versions follow. They are white on white, so switch the doc to dark mode to see them before you save them.

![](media/image4.png)

![](media/image5.png)

Reference: ask Claus for VBA_App_Designoplaeg_en.pdf (82 pages, every screen in light and dark) and VBA_App_Wireframes_en.pdf. The profile and settings screens are closest to what you build.

## 6. Step by step

Eight phases from an empty folder to the presentation. Each phase ends with a checkpoint or a gate - a gate is where Claus says yes before you move on.

### Phase 0 - Set up (19-26 October)

1.  Install Git, Python 3.12, VS Code, the Claude desktop app and Claude Code for the terminal (see the quickstart in section 10). Order Claude Pro and sign in with it.

2.  Work through the Must items in section 10 in one day, and send Test 1 by Monday 26 October, 12:00 Goa time (section 11). The optional courses wait until you are on plan.

3.  Create a private repository expert-selfservice on your own GitHub account, and give Claus read access. Open it in Claude Code and run /init.

4.  Edit CLAUDE.md so it holds the purpose (section 1), the ground rules (section 3), the stack (phase 3), the colour rule (section 5) and "always show me a plan before you write code".

5.  Create LEARNING_LOG.md and write the first entry.

Checkpoint: the repository exists with CLAUDE.md committed.

### Phase 1 - Understand and specify (26-28 October)

1.  Copy the user stories from section 4 into docs/stories.md. Export the structure of the skønsmandsdatabase yourself, as ground rule 1 in section 3 says, and put it in schema/.

2.  Ask Claude: "Read schema/. Explain every table and column in plain English. Mark anything you are guessing." Save the answer as docs/data-dictionary.md. Correct the guesses - ask Claus when you don't know.

3.  Map each user story in section 4 to the tables and columns it touches. What is missing goes into new tables with the prefix ss\_ - login codes, pending changes, change log. The original tables stay untouched.

4.  Write docs/SPEC.md with Claude: every screen, every field, every validation rule, which changes need approval. Let Claude interview you first:

Read schema/, docs/data-dictionary.md and the user stories in docs/stories.md.

Before you write SPEC.md, ask me up to 10 questions about anything unclear.

One question at a time.

Gate 1, Wednesday 28 October: Claus approves SPEC.md (30 minutes).

### Phase 2 - Test database (28-30 October)

1.  Create the database from the schema. Default is SQLite - one file, nothing to install - with the same table and column names as the original.

2.  Have Claude write seed.py with Faker in the Danish locale (da_DK): 100 experts and a fixed random seed, so you get the same 100 every time.

3.  Make the data realistic: real Danish postcodes and cities, CVR numbers that pass the modulus-11 check, 1-4 fields of expertise per expert from the fixed list, some experts with two languages and about 10 % with no phone number, so you test empty fields.

4.  Add three demo experts and two secretariat users with known emails, e.g. anna.demo@example.com.

5.  Write check_data.py: exactly 100 experts, no duplicate emails, all keys valid, every email ends in @example.com.

6.  One command rebuilds it all: python seed.py --reset. You run it before every demo.

Gate 2, Friday 30 October: Claus sees the 100 experts and the reset.

### Phase 3 - Architecture and skeleton (30 October - 2 November)

1.  Ask Claude for 2-3 stack options with pros and cons for a four-week MVP. Write your choice and the reason in docs/decisions.md. Default, unless Claus agrees to something else: Python, FastAPI, Jinja2 templates (HTML from the server, no JavaScript framework), SQLite, plain CSS and pytest.

2.  Build the skeleton: the app starts, shows the VBA top bar using static/tokens.css, loads its text from i18n/, and one test passes.

3.  Add a formatter and linter (ruff) and one command that runs all tests.

Checkpoint: the app starts on a clean folder by following the README.

### Phase 4 - The expert side (2-10 November)

Build in this order: E1 login, E2 profile, E3 address and phone, E4 email, E5 name and title, E6 company, E7 fields of expertise. Use the same loop for every story:

1.  Plan mode: ask for the plan and correct it.

2.  Ask for the tests first. Run them and watch them fail.

3.  Let Claude build until the tests pass.

4.  Review the diff with the checklist in section 7.

5.  Click through it yourself in the browser, also at 360 px width.

6.  Commit with a clear message and write in LEARNING_LOG.md.

### Phase 5 - The secretariat side (10-13 November)

1.  Build S1 search, S2 approve or reject and S3 change log with the same loop.

2.  Then the Should stories E8 and E9, if there is time.

Gate 3, Friday 13 November: all 10 Must stories work. Claus is travelling that week, so you send a screen recording of all 10 stories, and Claus tests them himself when he is back.

### Phase 6 - Test and harden (16-20 November)

1.  Run /code-review and /security-review. Fix every finding, or write in docs/decisions.md why not.

2.  Ask Claude to attack the app:

Act as a dishonest expert logged in as anna.demo@example.com.

List every way you could read or change another expert's data.

For each one, write a test that proves it is blocked.

3.  Test on a real phone, or in the browser's device mode at 360 px.

4.  Fresh install: clone the repository into a new folder and follow only the README.

5.  Record a 3-minute screen video of the whole demo. It is your backup if something fails on the day.

Gate 4, Friday 20 November: code freeze. After this, bug fixes only.

### Phase 7 - Presentation (23-27 November)

See section 9. Dry run with Claus on Tuesday 24 November, presentation on Friday 27 November.

Stretch - an MCP server (only if Gate 3 was on time)

Build a small MCP server that gives Claude read-only access to the test database, with three tools: search_experts, get_expert and list_pending_changes. Connect it to Claude and let Claude answer "Which experts with a postcode from 8000 to 8999 have changes waiting for approval?". It shows that you understand how Claude connects to other systems. Read-only, test database only.

## 7. Reviewing Claude's work

Review every diff before you commit it, as if a colleague you don't fully trust wrote it. Go through this list every time - it takes five minutes and catches most of what goes wrong:

- [ ] I can explain every changed line.

- [ ] It does what I asked and nothing more - no extra features, files or libraries.

- [ ] A new test exists, I saw it fail before the change, and all tests pass now.

- [ ] Every query for expert data filters on the logged-in expert's own ID, taken from the session - never from the URL or a form field.

- [ ] SQL uses parameters only, never strings glued together.

- [ ] Input is validated on the server, not only in the browser.

- [ ] The user sees a plain error message, never a stack trace.

- [ ] No real data, no secrets, no .env in the diff.

- [ ] Text sits in the i18n files, colours come from the tokens, and it works at 360 px.

- [ ] A new library has a reason in docs/decisions.md and is actively maintained.

Three questions that make Claude review itself:

Explain this diff as if I were reviewing a colleague's pull request.

Which three lines carry the most risk, and why?

What did you assume that I did not tell you?

When Claude is wrong, say exactly what is wrong and why. "Fix it" teaches it nothing. If the same fix fails twice, stop: undo with Git, run /clear, and start again with what you have learned written into the request. Every time you catch Claude in a mistake, note it in LEARNING_LOG.md - those notes are the most convincing part of your presentation.

## 8. Project plan

Phase 4 is the longest because the seven expert stories are the core of the MVP. If a gate slips, the Should and Could stories go first - the date 27 November does not move.

Every Friday you send the status report in section 12. The test deadlines in section 11 fall on the same Fridays.

## 9. The presentation on 27 November

Friday 27 November at 14:00 Goa time (09:30 Danish time), with Claus. You have 20 minutes plus 10 for questions. Most of it is a live demo - show the app, don't describe it.

| **Part**                  | **Minutes** | **Content**                                                                                         |
|---------------------------|-------------|-----------------------------------------------------------------------------------------------------|
| The problem               | 2           | Who the experts are, and how their changes are handled today                                        |
| Live demo                 | 8           | The script below                                                                                    |
| How I worked with Claude  | 5           | One good prompt, one mistake you caught, numbers from Git and your log: commits, tests, corrections |
| Architecture and security | 3           | One drawing of the parts, and how an expert is kept out of other experts' data                      |
| What's next               | 2           | What a real version would need                                                                      |

Demo script:

1.  Before anyone joins: run python seed.py --reset, start the app and open two browser windows - expert on the left, secretariat on the right.

2.  Log in as anna.demo@example.com and fetch the code from the dev mailbox.

3.  Change the address. Type postcode 8000 and Aarhus C appears by itself. Save, then show the change-log entry.

4.  Change the email and confirm it with the code sent to the new address.

5.  Add a field of expertise. It shows "Pending approval".

6.  Approve it in the secretariat window. Back in Anna's window it shows as approved.

7.  Put another expert's ID in the URL. The app refuses.

8.  Switch to Danish and shrink the window to phone width.

Keep the slides to eight at most. Have the video from phase 6 ready in case the live demo fails.

## 10. Learning material - before you start

Work through the Must items in one day, before phase 0 step 3. Test 1 checks them. The optional items are for later, once you are on plan. Where a video and the docs disagree, the docs win - Claude Code changes every month.

| **\#** | **What**                                                                                                                                                                                          | **From**                       | **Why**                                                                       | **Priority** |
|--------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------|-------------------------------------------------------------------------------|--------------|
| 1      | [Claude Code 101](https://academy.claude.com/courses/claude-code-101) - course, 12 lessons, about 1.5 hours, quiz and badge                                                                | Claude Academy                 | The agentic loop, Plan mode, the explore-plan-code-commit workflow, CLAUDE.md | Must         |
| 2      | [Claude Code in Action](https://anthropic.skilljar.com/claude-code-in-action) - course, free                                                                                               | Anthropic Academy              | Steering long sessions, permission modes, verifying Claude's work             | Must         |
| 3      | [Claude Code best practices \| Code w/ Claude](https://www.youtube.com/watch?v=gv0WHhKelSE) - video, May 2025                                                                              | Anthropic on YouTube           | How Anthropic's own staff work with Claude Code. Some commands are older      | Must         |
| 4      | [Quickstart](https://code.claude.com/docs/en/quickstart) and [Best practices](https://code.claude.com/docs/en/best-practices)                                                       | Claude Code docs               | The current, official way to do it                                            | Must         |
| 5      | [Commands](https://code.claude.com/docs/en/commands) and [Desktop app](https://code.claude.com/docs/en/desktop)                                                                     | Claude Code docs               | Reference - look things up here when you need them                            | Must         |
| 6      | [Claude Code for Beginners Tutorial \[Full Course\]](https://www.youtube.com/watch?v=gh2_PhgZGsM) - video                                                                                  | freeCodeCamp.org on YouTube    | A whole build from start to finish. Pick the chapters you need                | Optional     |
| 7      | [Claude Code: A Highly Agentic Coding Assistant](https://www.deeplearning.ai/courses/claude-code-a-highly-agentic-coding-assistant) - course, about 2 hours, free during the platform beta | DeepLearning.AI with Anthropic | Testing, debugging and building a web app from a mock-up                      | Optional     |
| 8      | [Introduction to Model Context Protocol](https://anthropic.skilljar.com/introduction-to-model-context-protocol) - course, free                                                             | Anthropic Academy              | Only before the MCP stretch at the end of section 6                           | Stretch      |

All links were checked on 16 October 2026.

## 11. Tests - Claus scores how you work

Five tests show Claus how you work with Claude, not only what you build. You do them in Claude Code and email the result to Claus by the deadline. Run the tests in the terminal version of Claude Code (claude in the VS Code terminal), so you can save the whole session with /export test-2.txt and attach the file.

| **Test**                      | **Deadline**        | **What you do**                                                                | **What you send**                                                                |
|-------------------------------|---------------------|--------------------------------------------------------------------------------|----------------------------------------------------------------------------------|
| T1 Claude basics              | Mon 26 Oct, 12:00   | Answer the 10 questions below in your own words, without Claude's help         | Your answers in the email                                                        |
| T2 Specification              | Wed 28 Oct (Gate 1) | Let Claude interview you and write SPEC.md - phase 1 step 4                    | SPEC.md and the exported session                                                 |
| T3 Code review                | Fri 30 Oct          | Find what is wrong in the code below. First 30 minutes alone, then with Claude | Your own list, Claude's list, and which of Claude's findings you checked and how |
| T4 One story, start to finish | Fri 6 Nov           | Build E3 in one session: plan, tests first, build, review, commit              | The exported session, git log for the story and your review notes                |
| T5 Break your own app         | Fri 20 Nov (Gate 4) | Phase 6 step 2: attack the app and write a test for every attack               | The list of attacks, the tests, what you fixed and the /security-review output   |

Claus scores each test from 1 to 4 in four areas:

| **Area**          | **1**                               | **4**                                |
|-------------------|-------------------------------------|--------------------------------------|
| Steering          | Claude decided what to build        | You decided, Claude carried it out   |
| Checking          | Claude's output accepted as it came | Every claim checked, mistakes caught |
| Understanding     | You can't explain the result        | You can explain every line           |
| Data and security | A ground rule from section 3 broken | All rules followed                   |

An average of 3 is a pass. A broken ground rule is talked through with Claus the same day, whatever the score.

T1 - ten questions

1.  What is CLAUDE.md, and when does Claude read it?

2.  What does Plan mode do, and why use it before Claude writes code?

3.  When do you use /clear, and what does it keep?

4.  Claude says "all tests pass". What do you do before you believe it?

5.  Name three things you must never paste into Claude in this project.

6.  A step went wrong and Claude changed six files. How do you get back to where you were?

7.  What does /security-review look at?

8.  Why does every query for expert data take the expert ID from the session and not from the URL?

9.  Why do you ask Claude for the test before the code?

10. What is an MCP server, in one sentence?

T3 - the code to review

Claude wrote this for stories E3 and E4. Find everything that is wrong with it - bugs, security holes and broken rules from this guide. Write each finding as one line: what is wrong, why it matters, how to fix it.

from fastapi import FastAPI, Form, Request

import random, sqlite3

app = FastAPI(debug=True)

SECRET_KEY = "vba-selfservice-2026"

db = sqlite3.connect("experts.db", check_same_thread=False)

@app.post("/login/code")

def send_code(email: str = Form(...)):

code = str(random.randint(100000, 999999))

db.execute("INSERT INTO ss_login_codes (email, code) VALUES (?, ?)", (email, code))

db.commit()

print(f"Login code for {email}: {code}")

return {"sent": True}

@app.post("/login/verify")

def verify(request: Request, email: str = Form(...), code: str = Form(...)):

row = db.execute("SELECT code FROM ss_login_codes WHERE email = ? ORDER BY id DESC",

(email,)).fetchone()

if row and row\[0\] == code:

expert = db.execute("SELECT id FROM experts WHERE email = ?", (email,)).fetchone()

request.session\["expert_id"\] = expert\[0\]

return {"ok": True}

return {"ok": False}

@app.post("/experts/{expert_id}/address")

def update_address(expert_id: int, street: str = Form(...), postcode: str = Form(...)):

city = db.execute(f"SELECT city FROM postcodes WHERE postcode = '{postcode}'").fetchone()\[0\]

db.execute("UPDATE experts SET street = ?, postcode = ?, city = ? WHERE id = ?",

(street, postcode, city, expert_id))

db.commit()

return {"saved": True}

@app.post("/experts/{expert_id}/email")

def update_email(expert_id: int, new_email: str = Form(...)):

db.execute("UPDATE experts SET email = ? WHERE id = ?", (new_email, expert_id))

db.commit()

return {"saved": True}

## 12. Friday status report

Every Friday by 12:00 Goa time you email Claus a status, and Claus follows up with you at 14:00 Goa time. There are five: Monday 26 October, 30 October, 6 November, 13 November and 20 November. Claus is travelling on 6 and 13 November, so those two weeks have no follow-up call. Send the status in writing as usual - Claus emails you what else he needs those weeks. Copy this template:

Subject: Expert self-service - status week 43

Overall: green / yellow / red - one line why

Done this week: stories, gates and tests, by ID

Next week: what you will finish

Blocked: what you need from Claus, and by when

Claude this week: one thing that worked, one thing that went wrong

Hours spent:

Attached: LEARNING_LOG.md, and the test result if one is due

Green means on plan. Yellow means a gate is at risk, but you can recover this week. Red means a gate will slip - send red the day you know, don't wait for Friday.

You may let Claude draft the status from git log and LEARNING_LOG.md, but correct it before you send it. Claus reads it as your words.

## 13. Decisions and open points

Claus answered most of the open points on 16 October. Two remain.

Settled:

- Hours: Leesha has 30+ hours a week. The plan was built for about half time, so there is room for the Should stories.

- Claude: Leesha orders Claude Pro herself.

- Repository: Leesha creates a private GitHub repository herself.

- Database: Leesha already has access and exports the whole structure herself. Lookup lists without personal data come with their content.

- Logo and fonts: in section 5.

- Presentation: Friday 27 November at 14:00 Goa time (09:30 Danish time). Claus attends.

Still open:

- [ ] Approval rule: name, title and fields of expertise need approval, everything else is saved at once. Needed by Gate 1 on 28 Oct 2026.

- [ ] Times for the gates and for the dry run on Tuesday 24 November.

## 14. Glossary

| **Danish**                                           | **English**                                        | **What it means here**                                                              |
|------------------------------------------------------|----------------------------------------------------|-------------------------------------------------------------------------------------|
| VBA - Voldgiftsnævnet for Bygge- og Anlægsvirksomhed | Danish Building and Construction Arbitration Board | Settles construction disputes through arbitration, expert appraisal and mediation   |
| Skønsmand (plural skønsmænd)                         | Expert, appraiser                                  | An independent professional VBA appoints to a case, e.g. to assess building defects |
| Skønsmandsdatabasen                                  | The expert register                                | The database of experts VBA can appoint - the one you copy                          |
| Syn og skøn                                          | Survey and expert appraisal                        | The case type where an expert inspects and answers the parties' questions           |
| Sekretariatet                                        | The secretariat                                    | VBA's staff who run the cases and approve changes in your app                       |
| Faglige kompetencer                                  | Fields of expertise                                | A fixed list. The secretariat searches on it to find the right expert               |
| Uvildighedserklæring                                 | Declaration of impartiality                        | The expert confirms having no ties to the parties                                   |
| CVR-nummer                                           | Company registration number                        | 8 digits. The weighted sum (2-7-6-5-4-3-2-1) must be divisible by 11                |
| Postnummer                                           | Postcode                                           | 4 digits, each with one city name                                                   |
| MitID                                                | Danish national digital ID                         | The real login for Danes - out of scope here                                        |
| VAKS                                                 | VBA's current case system                          | Built on SharePoint                                                                 |
| VBA Portalen                                         | VBA's external portal                              | Where parties, lawyers and experts log in today                                     |
