"""Data rules shared by seed.py, check_data.py and (from Phase 3) the app.

Each rule is written here once. Don't copy these values into other files.
"""

import re

# Candidates.Status (docs/SPEC.md, open point C).
STANDBY = -1
RETIRED = 0
ACTIVE = 1
ACCEPTED = 2
OBSERVATION = 3
EVALUATED = 4
STATUS_NAMES = {
    STANDBY: "Standby",
    RETIRED: "Retired",
    ACTIVE: "Active",
    ACCEPTED: "Accepted",
    OBSERVATION: "Under observation",
    EVALUATED: "Evaluated",
}

# Test data.
DEMO_ANNA = "anna.demo@example.com"
DEMO_BO = "bo.demo@example.com"
DEMO_CARL = "carl.demo@example.com"
DEMO_EMAILS = (DEMO_ANNA, DEMO_BO, DEMO_CARL)
EXPECTED_EXPERTS = 100
EXPECTED_STAFF = 2
EMAIL_DOMAIN = "@example.com"
MIN_REGISTRATION_ID = 900001
NO_PHONE_EXPERTS = 10  # experts with no phone number at all
SKILLS_PER_EXPERT = (1, 4)  # fewest, most
ROLES_PER_EXPERT = (1, 2)  # fewest, most

# Regions.Id of the five Danish regions in data/lookups/Regions.csv.
DANISH_REGION_IDS = (4, 5, 6, 7, 8)

# An expert's postcode: 4 digits, in a Danish region. A SQL condition on
# Zipcodes AS z joined with Cities AS ci. The Ids are numbers, so putting them
# into the string is safe.
DANISH_POSTCODE_SQL = (
    "z.Zipcode GLOB '[0-9][0-9][0-9][0-9]' "
    f"AND ci.RegionId IN ({', '.join(str(i) for i in DANISH_REGION_IDS)})"
)

# Text limits of the Candidates columns, from schema/schema.sql.
MAX_LENGTHS = {
    "FirstName": 50,
    "MiddleName": 50,
    "LastName": 50,
    "Email": 50,
    "Street": 50,
    "PhoneMobile": 50,
    "PhoneWork": 50,
    "PhoneHome": 50,
    "Company": 250,
    "Designation": 250,
    "RegistrationId": 20,
}

# One @, no spaces, a name part of letters, digits and ._%+-, a domain with a dot.
EMAIL_PATTERN = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+")

CVR_WEIGHTS = (2, 7, 6, 5, 4, 3, 2, 1)


def is_valid_email(email: str | None) -> bool:
    """A plain email address such as anna.demo@example.com."""
    return bool(EMAIL_PATTERN.fullmatch(email or ""))


def cvr_is_valid(cvr: str | None) -> bool:
    """8 digits, and the weighted digit sum is divisible by 11."""
    if not re.fullmatch(r"\d{8}", cvr or ""):
        return False
    return sum(int(d) * w for d, w in zip(cvr, CVR_WEIGHTS, strict=True)) % 11 == 0
