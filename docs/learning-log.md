## Learning Log

** Decision which i took and asked claude and where i felt it deviated slightly away was when i asked it few questions which are listed below
(A) --reset must refuse to delete any path outside data/ or without a .db extension,
(B) reject PO-box and bulk-mail postcodes in check_data.py,
(C) check the 50-character limits against the schema and SPEC.

and it started to manipulate the code and started making the changes after this, in this process it created multiple files in tests and other folders which were not necessary so then i used the command /rewind and restored the context back to the stable version of the code.

This was a learning for me where i used the claude code command like /rewind.