import pytest

import check_data
from check_data import check
from rules import NO_PHONE_EXPERTS, cvr_is_valid


def has_problem(conn, text):
    return any(text in problem for problem in check(conn))


@pytest.mark.parametrize("cvr", ["97663440", "10297877", "10000009"])
def test_valid_cvr(cvr):
    assert cvr_is_valid(cvr)


@pytest.mark.parametrize("cvr", ["97663441", "1234567", "123456789", "abcdefgh", "", None])
def test_invalid_cvr(cvr):
    assert not cvr_is_valid(cvr)


def test_catches_email_outside_example_com(db):
    db.execute("UPDATE Candidates SET Email = 'someone@gmail.com' WHERE Id = 4")
    assert has_problem(db, "doesn't end in @example.com")


def test_catches_duplicate_email(db):
    db.execute("UPDATE Candidates SET Email = 'ANNA.DEMO@example.com' WHERE Id = 4")
    assert has_problem(db, "anna.demo@example.com is used 2 times")


def test_catches_unknown_skill(db):
    db.execute("INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (4, 'Fri tekst')")
    assert has_problem(db, "not in Fags")


def test_catches_bad_cvr(db):
    db.execute("UPDATE ss_expert_company SET cvr = '12345678' WHERE candidate_id = 1")
    assert has_problem(db, "invalid CVR")


def test_catches_101_experts(db):
    db.execute(
        "INSERT INTO Candidates (FirstName, LastName, Email, RegistrationId, Status, ZipcodeId) "
        "SELECT 'Ekstra', 'Ekspert', 'ekstra@example.com', '999999', 1, ZipcodeId "
        "FROM Candidates WHERE Id = 1"
    )
    assert has_problem(db, "Expected 100 experts, found 101")


def test_catches_broken_key(db):
    db.execute("PRAGMA foreign_keys = OFF")
    db.execute("UPDATE Candidates SET ZipcodeId = 999999 WHERE Id = 4")
    assert has_problem(db, "points to a missing Zipcodes row")


def test_catches_retired_demo_expert(db):
    db.execute("UPDATE Candidates SET Status = 0 WHERE Email = 'anna.demo@example.com'")
    assert has_problem(db, "is Retired")


def test_catches_four_digit_postcode_outside_denmark(db):
    # Norway also has 4-digit postcodes. City 1275 is OSLO, region Norge.
    db.execute("INSERT INTO Zipcodes (Id, CityId, Zipcode) VALUES (9001, 1275, '0150')")
    db.execute("UPDATE Candidates SET ZipcodeId = 9001 WHERE Id = 4")
    assert has_problem(db, "Expert 4 has no valid Danish postcode ('0150')")


@pytest.mark.parametrize(
    ("column", "length", "limit"),
    [("Company", 251, 250), ("Designation", 251, 250), ("PhoneMobile", 51, 50)],
)
def test_catches_too_long_text(db, column, length, limit):
    db.execute(f'UPDATE Candidates SET "{column}" = ? WHERE Id = 4', ("x" * length,))
    assert has_problem(db, f"Expert 4 {column} is {length} characters, max is {limit}")


def test_catches_too_long_staff_email(db):
    email = "s" * 40 + "@example.com"
    db.execute("UPDATE ss_staff_users SET email = ? WHERE id = 1", (email,))
    assert has_problem(db, "Staff user 1 email is 52 characters, max is 50")


def test_missing_registration_id_is_reported_not_crashed(db):
    db.execute("UPDATE Candidates SET RegistrationId = NULL WHERE Id = 4")
    assert has_problem(db, "Expert 4 RegistrationId None is not a test number")


def test_catches_wrong_number_without_phone(db):
    (expert_id,) = db.execute(
        "SELECT Id FROM Candidates "
        "WHERE PhoneMobile IS NULL AND PhoneWork IS NULL AND PhoneHome IS NULL LIMIT 1"
    ).fetchone()
    db.execute("UPDATE Candidates SET PhoneMobile = '12345678' WHERE Id = ?", (expert_id,))
    expected = f"{NO_PHONE_EXPERTS - 1} experts have no phone, expected {NO_PHONE_EXPERTS}"
    assert has_problem(db, expected)


def test_demo_email_found_in_any_case(db):
    db.execute("UPDATE Candidates SET Email = 'Anna.Demo@example.com' WHERE Id = 1")
    assert not has_problem(db, "is missing")


@pytest.mark.parametrize("email", ["@example.com", "a@b@example.com", "x y@example.com"])
def test_catches_malformed_email(db, email):
    db.execute("UPDATE Candidates SET Email = ? WHERE Id = 4", (email,))
    assert has_problem(db, "is not a valid email")


def test_catches_three_roles(db):
    # Carl (Id 3) has 2 roles: Skønsmand and Mediator. Add a third.
    db.execute("INSERT INTO CandidateTags (CandidateId, TagId) VALUES (3, 40)")
    assert has_problem(db, "Expert 3 has 3 roles, expected 1-2")


def test_catches_same_role_twice(db):
    # Anna (Id 1) has 1 role, Skønsmand (35). Add it again.
    db.execute("INSERT INTO CandidateTags (CandidateId, TagId) VALUES (1, 35)")
    assert has_problem(db, "Expert 1 has one of their roles twice")


def test_empty_skill_is_not_reported_as_duplicate(db):
    db.execute("INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (2, NULL)")
    problems = check(db)
    assert "Expert 2 has skill None, not in Fags" in problems
    assert not any("Expert 2 has one of their skills twice" in p for p in problems)


def test_main_accepts_relative_path(seeded_db, monkeypatch, capsys):
    monkeypatch.chdir(seeded_db.parent)
    assert check_data.main([seeded_db.name]) == 0
    assert "check_data: OK" in capsys.readouterr().out
