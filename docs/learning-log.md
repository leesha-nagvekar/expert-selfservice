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