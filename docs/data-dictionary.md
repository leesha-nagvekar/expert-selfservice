# Data dictionary

Plain-English explanation of the tables in `schema/schema.sql` that matter for the app. That file is a structure-only export of VBA's expert register (SQL Server database `CVApp2`, 49 tables, saved as UTF-16). It holds no rows. It stays on the developer's machine and is not in Git.

This file explains 35 tables. The other 14 are left out on purpose, because the app never uses them: the seven `_Temp` tables (`Candidates_Temp`, `CandidateTags_Temp`, `CandidateSkills_Temp`, `CandidateCourses_Temp`, `CandidatePreferredRegions_Temp`, `CandidateFiles_Temp`, `AttachedFiles_Temp`), `RATINGS`, `Documents`, `DocumentGroups`, `old_documents`, `contact_info`, `KCOPYPATH` and `test`.

- **(guess)**: worked out from the name or type only. Still to be checked with Claus.
- **(confirmed)**: answered by the developer on 27 Oct 2026.
- **Used**: the table is created in the SQLite test database and used by the app. All other tables are left out (decision 8 in `docs/SPEC.md`).

"Candidate" is the database's word for an expert (skønsmand).

## 1. Experts

### Candidates (used)
One row per expert.

| Column | Type | Meaning |
|---|---|---|
| Id | int, auto | Internal number, set by the database. |
| FirstName | text 50, required | First name. Change needs approval. |
| LastName | text 50, required | Last name. Change needs approval. |
| MiddleName | text 50 | Middle name. Change needs approval. |
| FirstName2, LastName2 | text 50 | A second name form, e.g. the name used professionally (guess). Not shown in the app. |
| FirstNameOld, LastNameOld | text 50 | The name before a name change (guess). Not shown in the app. |
| RegistrationId | text 20 | The VBA number (confirmed). A whole number with 1 or more digits, stored as text (confirmed). Read-only, "Set by VBA". |
| PhoneHome | text 50 | Home phone. Optional. |
| PhoneWork | text 50 | Work phone. Optional. |
| PhoneMobile | text 50 | Mobile phone. Optional. |
| Address | text 500 | Address as free text. Not shown or changed by the app (confirmed). |
| Street | text 50 | Street and house number. Edited by the expert (confirmed). |
| ZipcodeId | int → Zipcodes.Id | The expert's postcode, which also gives the city. |
| Stat_Provins | text 50 | State or province. Not shown. |
| Notes | text, long | Internal notes by the secretariat . Never shown to the expert. |
| Company | text 250 | Company name. Saved at once. |
| Designation | text 250 | The expert's job title (confirmed). Saved at once. |
| Website | text 250 | Website address. Not part of the MVP. |
| statusOld | yes/no | A yes/no status from an older version of the system (guess). It can't hold a status such as Standby: that is what `OldStatus` does. Check with Claus. |
| LastUpdated | date | Date the record was last changed. The app sets it on every change. |
| Email | text 50 | Email. Used for login. Max 50 characters. |
| Fax | text 50 | Fax number. Not part of the MVP. |
| Ratings | int | A rating from the secretariat. Internal, never shown to the expert. |
| Bygherre | yes/no | The expert has a background on the client side (bygherre). |
| Rådgiver | yes/no | The expert has a background as a consultant (rådgiver). |
| Entreprenør | yes/no | The expert has a background as a contractor (entreprenør). |
| Created | date | Date the expert was added. Read-only, "Set by VBA". |
| LastAccessed | date | Date the record was last opened. |
| Status | int, required, default -1 | Current status (confirmed, list may not be complete): -1 Standby, 0 Udgået (Retired), 1 Aktiv (Active), 2 Optag (Accepted), 3 Observation (Under observation), 4 Evalueret (Evaluated). Read-only. |
| OldStatus | int | The status before Standby. The expert goes back to it when UntilDate passes (confirmed). |
| UntilDate | date | The date a Standby ends (confirmed). |


## 2. Postcodes and regions

### Zipcodes (used)
| Column | Meaning |
|---|---|
| Id | Internal number. Candidates.ZipcodeId points here. |
| CityId | → Cities.Id: the city for this postcode. |
| Zipcode | The postcode as text, e.g. "8000". |

### Cities (used)
| Column | Meaning |
|---|---|
| Id | Internal number. |
| City | City name, e.g. "Aarhus C". |
| RegionId | → Regions.Id. |
| ZipCode | The postcode again (duplicate of Zipcodes.Zipcode). |

### Regions (used)
| Column | Meaning |
|---|---|
| Id | Internal number. |
| RegionName | Name of the region. |
| ShowInFilter | Yes/no: shown as a filter in the secretariat's search. |
| IsAutoSelected | Yes/no: ticked by default in that filter . |
| DisplayOrder | Sort order, default 1000. |

### CandidatePreferredRegions
Regions where the expert wants to take cases: `Id`, `CandidateId` → Candidates, `RegionId` → Regions. Not part of the MVP.


## 3. Fields of expertise and roles

### Fags (used)
VBA's fixed list of fields of expertise (confirmed), e.g. Konstruktioner, Generalist, Installationer, Tømrer. Maintained by VBA only: VBA adds, renames and deletes entries. Experts pick from it but never add to or change it (confirmed 28 Oct 2026).

| Column | Meaning |
|---|---|
| Id | Internal number. Note: the table has no primary key. |
| Fag | The field name in Danish. |

### CandidateSkills (used)
The fields of expertise of each expert, one row per field (confirmed).

| Column | Meaning |
|---|---|
| Id | Internal number. |
| CandidateId | → Candidates.Id. |
| Skill | The field name **as text**, copied from Fags.Fag (confirmed). There is no foreign key, so the app checks the name against Fags. |


### ProfessionTags (used)
The roles an expert can hold (confirmed), e.g. Skønsmand, Sagkyndig, Mediator, Enedommer.

| Column | Meaning |
|---|---|
| Id | Internal number. |
| Tag | Role name in Danish. |
| ShowInFilter | Yes/no: shown as a filter in search. |

### CandidateTags (used)
The roles of each expert, several per expert (confirmed): `Id`, `CandidateId` → Candidates, `TagId` → ProfessionTags. Shown as the expert's role. Read-only, "Set by VBA": VBA appoints, the expert does not (confirmed).

## 4. Courses

### Courses
VBA's courses for experts: `Id`, `CourseName`, `Logo` (image file ). Not part of the MVP.

### CandidateCourses
Courses an expert has taken: `Id`, `CandidateId`, `CourseId` → Courses, `CourseAttended` (date). Not part of the MVP.

## 5. Files

### CandidateFiles
Files per expert, e.g. a CV.

| Column | Meaning |
|---|---|
| Id | Internal number. |
| CandidateId | → Candidates.Id. |
| FileContent | These are pdf files uploaded by the Experts |
| FileName | File name. |
| CreationDateTime | When it was added. Default 1 Jan 1900, It shows when the file was uploaded by an expert. |
| IsPrimary | Yes/no: the main file, e.g. the current CV. |

Possible use for E10 (CV upload, a Could story).


### AttachedFiles
Other attachments per expert. Same columns as CandidateFiles but without IsPrimary. Not used.

### Attachment
Attachments every time uploaded by an Expert, records of all the attachments as text form: `T_ID`, `CID` (expert), `File_Path`, `FileMatter` (file text from the CV), `FileName` (Name of the CV ).

## 6. Secretariat tools

### CandidateChangeLog
The existing change log: `Id`, `CandidateId`, `Log` (free-text description of the change), `Date`, `UserName` (who made it). It has no separate old and new values, so the app writes to `ss_change_log` instead and leaves this table untouched (decision 3).

### CandidateNotes
The secretariat's notes about an expert: `Id`, `CandidateId`, `Note`, `Date`, `UserName`. Internal. Not used.

### Favoritter
The secretariat's saved lists of experts ("favourites"): `Id`, `Name`. Not used.

### FavoritterCandidates
Experts on each list: `Id`, `CandidateId`, `FavoritteId` → Favoritter. Deleting a list deletes its rows. Not used.


## 7. Old backups, migration and file copying (not used)

### Candidates backups and test copies
These hold the same columns as Candidates at an earlier point in time:
- **candidates_bkp190617**: Candidates up to `Status`, without the name-history and standby columns.
- **candidatesBKP_12_05_2017**: up to `LastAccessed`. `Status` is a yes/no here.
- **CandidatesBKP_20_04_2017**, **CandidatesTest**, **candRTemp**: up to `Entreprenør`. `Status` is a yes/no.
- **Candidates_EmailBackup**: `Id`, `Email`. A copy of the emails.

### Other backups
- **CandidateTags_bkp08JUN2017**: `Id`, `CandidateId`, `TagId`.
- **CandidateSkillsCopy**, **CandidateSkillsCopy2**: `Id`, `CandidateId`, `Skill`.
- **Cities_bkp15062017**: same columns as Cities.
- **Zipcodes_bkp15062017**: same columns as Zipcodes.
- **regions_bkp15062017**: same columns as Regions.

### Migration helpers
When VBA moved from its old expert system to today's database, the experts were copied over once. These tables are the scratch paper from that one-time move. Nothing uses them today, and the app doesn't touch them.
- **temp1**: the old-to-new number list. For each expert it holds the number in the old system (`old_id`) and the new `Candidates.Id` (`ID`), with the name in the old form (`First_Name`, `Last_Name`) and the new form (`FirstName`, `LastName`), so each match could be checked. 
- **tempPassive**: the experts who were set to passive during the move: `id`, `NewID` (new Candidates.Id), `Old_Id`, `RegistrationId`, `First_Name`, `FirstName`, the old `Status` (text) and `NewStatus` (yes/no) (guess).
- **fagCompare**: a translation list for fields of expertise. `OldFag` (the old system's name) → `NewFag` (the Fags name it became), plus `Title` (guess).
- **Old_Region**: the old system's regions with postcodes and cities: `REGION`, `ZIPCODE`, `CITY`, `RID` (internal number), `RGID` (region group, guess). Replaced by Regions, Cities and Zipcodes.

### File copying
- **COPYPATH**: `CPID`, `CPATH` (file path), `UID`, `USRID` (user ids), `UNAME` (computer name, e.g. `PC01.EXAMPLE.LOCAL`), `FLAG` (done yes/no, default no). The folder path where a file is stored after an expert uploads it, e.g. `\\server\CV\AttachedFiles\<expert id>\<file name>.pdf`.

## 8. New self-service tables (prefix ss_)
Defined in `docs/SPEC.md` section 5.

## 9. Questions and answers (schema interview, 27–28 Oct 2026)

Claude asked these questions one at a time while working through `schema/` and `docs/stories.md`. Typed answers are quoted word for word. Where the developer picked one of the offered options, the chosen option is shown.

**Q1. Which table holds VBA's fixed list of fields of expertise (story E7, and what the secretariat searches on in S1)?**
Options: ProfessionTags / Fags / CandidateSkills / Don't know, ask Claus.
A: "so basically Fags have skills like "Konstruktioner, Generalist, Installationer,Tømrer, etc." these are in Danish and the ProfessionTags have "Skønsmand, Sagkyndig, Mediator, Enedommer" are in danish. CandidateSkills are the skills attached to each candidate from the table Fags. it may be like for e.g. Candidate with ID = 1 may have multiple skills "1-Konstruktioner, 2-Generalist, 3-Installationer" also for CandidateTags the candidate ID=1 may have multiple tags "Skønsmand, Sagkyndig"."

**Q1b. What exactly is stored in CandidateSkills.Skill? It's a text column with no foreign key to Fags.**
Options: The Fag name as text / The Fags.Id as text / Id and name together / Not sure, ask Claus.
A: "The CandidateSkills table saves the Skill in text (nvarchar) only the CandidateID is Int and skill is the skill from the "Fags" table"

**Q2. Where should changes waiting for approval (name, title, fields of expertise) be kept until the secretariat decides?**
Options: New ss_pending_changes / Reuse the _Temp tables / Ask Claus.
A: New ss_pending_changes.

**Q3. The existing CandidateChangeLog only has free text (Log, Date, UserName). S3 needs who, what, when, old value and new value. Where should the app write its change log?**
Options: Only ss_change_log / Both tables / Ask Claus.
A: Only ss_change_log.

**Q4. E5 says 'title' (goes to approval) and E6 says 'my role' at the company (saved at once). Candidates has only one matching column, Designation. How should these be stored?**
Options: Designation = title, new column for role / Designation = role, new column for title / Same thing, one field / Ask Claus.
A: "so basically since Candidate is linked to other tables such as Skills , ProfessionTags, etc. E5 - Title will be the [Designation] column from Candidates and Role will probably be the CandidateTags - which is the Profession Tags linked to the candidate. So its a whole picture view it as a tree form which with candidate as main and liked to multiple tables and all data is gathered and got into one whole bundle."

**Q4b. When an expert changes their role (ProfessionTags, e.g. adds 'Mediator'), what should happen?**
Options: Goes to approval / Saved at once, as E6 says / Read-only, set by VBA / Ask Claus.
A: "Yes I think the Roles are assigned by VBA, i have asked Claus about this and we will confirm on this"
Later settled by the Field Rule in `docs/SPEC.md` section 3: roles are read-only, marked "Set by VBA". VBA appoints, the expert does not.

**Q5. Candidates has two address columns: Address (free text, up to 500 characters) and Street (up to 50 characters), plus ZipcodeId for postcode and city. Which one does the expert edit in E3?**
Options: Street + ZipcodeId / Address + ZipcodeId / Both / Ask Claus.
A: Street + ZipcodeId.

**Q6. Candidates has three phone columns: PhoneHome, PhoneWork and PhoneMobile. Which ones can the expert see and edit in E3?**
Options: All three / Mobile only / Mobile and work / Ask Claus.
A: All three.

**Q7. E2 shows a read-only 'VBA number' marked 'Set by VBA'. Is that the RegistrationId column in Candidates (text, up to 20 characters)?**
Options: Yes, RegistrationId / No, it's Candidates.Id / Ask Claus.
A: Yes, RegistrationId. (The format was not given; open point B in `docs/SPEC.md`.)

**Q8. Which tables from schema.sql should go into the SQLite test database?**
Options: Only those the app uses / All core tables, no legacy / All tables / Ask Claus.
A: Only those the app uses.

**Q9. Candidates.Status is a number (default -1), with OldStatus and UntilDate next to it. What do the values mean, and which experts may log in to the self-service app?**
Options: Don't know, ask Claus / Only active experts log in / Everyone logs in.
A: "Standby = -1, Udgået = 0,Aktiv = 1,Optag = 2,Observation = 3,Evalueret = 4, (u can change these from danish to english) these are some of the statuses and UntilDate is the date is for status "standby" until the date the candidate wil be in standby status once the date is changed it will come to previous state, OldStatus saves the previous status of the candidate"

**Q9b. Which statuses may log in to the self-service app?**
Options: All except Retired / Everyone / Only Active / Ask Claus.
A: All except Retired.

**Q10. Approval rule: which changes go to the secretariat as 'Pending approval', and which are saved at once?**
Options: As in the guide / Guide + company to approval / Ask Claus at Gate 1.
A: As in the guide.
Later replaced by the Field Rule the developer added to `docs/SPEC.md` section 3: Designation (job title) is saved at once; only first name, last name and fields of expertise go to approval.

**Q11. Who adds, renames or deletes entries in the Fags list (the fields of expertise)?** (28 Oct 2026)
Options: VBA, outside this app / Secretariat, inside this app.
A: "VBA maintains the addition, deletion or editing of Fags, Skills etc."
The expert only picks from Fags. The app has no screen for maintaining Fags, because no story asks for one.

**Q12. Several tables were taken out of data-dictionary.md (the _Temp tables, RATINGS, Documents, DocumentGroups, old_documents, contact_info, KCOPYPATH, test). Was that on purpose?**
Options: Yes, keep them out / No, put them back.
A: Yes, keep them out.

**Q13. What pattern does RegistrationId (the VBA number) follow? (Pattern only, never a real number.)**
Options: Not known yet / Digits only.
A: "It is an INTEGER having 1 or more digits."

**Q14. Status 2, 'Optag': which English word should the app and the docs use?**
Options: Accepted / Being admitted.
A: Accepted.

## 10. Questions and answers (Phase 2 test database, 28–29 Oct 2026)

Claude asked these while planning the SQLite test database (`seed.py`, `check_data.py`). Same format as section 9: typed answers are quoted word for word, and where the developer picked an option, the option is shown.

**Q15. The lookup lists (Fags, ProfessionTags, Zipcodes, Cities, Regions) are not in schema/ yet. Only schema.sql is there, and it has no rows. Where should seed.py get them from?**
Options: Wait for your export / Stand-in list now / Public postcodes + stand-in Fags.
A: "I will give the export of the above 5 files in /schema so that you get an idea how these files look like. after you get the idea of what the files look like and data, delete these files, the you create your own data look up files,feed them with more data as per the requirement of project. like you will create the other table data. … any thing unclear please ask."
Done: the exports were read, checked for personal data (none), and written as our own files in `data/lookups/`. The exports were then deleted.

**Q15b. 'Feed them with more data': which lists should get more rows?** (28 Oct 2026)
Options: Postcodes only / All five lists / Copy exactly, add nothing.
A: Postcodes only.
Zipcodes and Cities get all 1,089 normal Danish postcodes from GeoNames (CC BY 4.0), with Ids from 2001. The public DAWA API closed in 2026, and Datafordeler needs an API key. PO-box and bulk-mail postcodes are left out.

**Q15c. Should the plan be written now, or after the export files arrive?**
Options: Plan now / Wait for the files.
A: "i have already added the files , please check and Plan"

**Q16. The guide asks for 'some experts with two languages', but the schema has no language column, and languages belong to E8 (Should, after Gate 3). What should the seed do?**
Options: Skip until E8 / New ss_expert_languages.
A: New ss_expert_languages.
Two new tables: `ss_languages` (the list) and `ss_expert_languages` (one row per expert and language). See `docs/SPEC.md` section 5.

**Q17. Are the 3 demo experts part of the 100, or extra? SPEC 7 said '100 fake experts plus 3 demo experts', but check_data.py should check 'exactly 100 experts'.**
Options: Part of the 100 / Extra: 103 rows.
A: Part of the 100.
Candidates holds 97 random experts and 3 demo experts (Anna, Bo, Carl), 100 rows in all.

**Q18. Some fake RegistrationIds (VBA numbers) could match real ones by chance. Which range should the test data use?**
Options: 900001 and up / Look like real ones.
A: 900001 and up.
Still digits only (Q13), but easy to spot as test data.

**Q19. Fags.csv has 10 rows (Fiber, Netværk, EL … Udstykning, Ids 282–298), but the four examples from the interview (Konstruktioner, Generalist, Installationer, Tømrer) are not among them. Is this the whole Fags list or a sample?**
Options: Sample: use 10 + the 4 / I'll send the full list / Use the 10 only.
A: Use the 10 only.
Each expert gets 1–4 fields of expertise from these 10.

**Q20. ProfessionTags.csv has 9 roles (Faglig dommer, Skønsmand, Sagkyndig, Mediator, Enedommer, Mægler, Opmand, Mentor, Staderegistrant). Is that the full list?**
Options: Yes, full list / Sample, use these 9.
A: "you can expand the list by adding more this is just for sample"
Three made-up roles were added: Voldgiftsdommer, Bisidder and Tilsynsførende (Ids 43–45). That gives 12 roles. Each expert holds 1–2 of them.

**Q21. OldStatus for a Standby expert.** Claude asked which values `OldStatus` may hold, either 1–4 only (a Retired expert can't be put on Standby) or 0–4. The question was first turned down and answered later, on 30 Oct 2026. The seed gives OldStatus a value from 1–4 (Active to Evaluated), and `check_data.py` checks that every Standby expert has an UntilDate and an OldStatus.
A: "Old Status is basically the status which was before changing the experts status to new one. for e.g. if Current status is : active , and now the expert changes it to standby, then the oldstatus will hold active.
