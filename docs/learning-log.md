## Learning Log

** Decision which i took and asked claude and where i felt it deviated slightly away was when i asked it few questions which are listed below
(A) --reset must refuse to delete any path outside data/ or without a .db extension,
(B) reject PO-box and bulk-mail postcodes in check_data.py,
(C) check the 50-character limits against the schema and SPEC.

and it started to manipulate the code and started making the changes after this, in this process it created multiple files in tests and other folders which were not necessary so then i used the command /rewind and restored the context back to the stable version of the code.

This was a learning for me where i used the claude code command like /rewind.

## 2 Nov
2 Nov: The stack changed from Python to ASP.NET Core. I saved the Python skeleton under a branch and tag instead of using /rewind, started a new branch from the last docs commit, and reused i18n, tokens and fonts. Claude in VS Code kept assuming Python until CLAUDE.md was updated - lesson: update CLAUDE.md first when requirements change.
