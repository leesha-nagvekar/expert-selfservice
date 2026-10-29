
## What the app must do

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