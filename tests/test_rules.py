import pytest

from rules import is_valid_email


@pytest.mark.parametrize(
    "email",
    [
        "anna.demo@example.com",
        "Anna.Demo@example.com",
        "soeren.oestergaard2@example.com",
        "anne-marie.berg+test@example.com",
        "a@sub.example.com",
    ],
)
def test_valid_email(email):
    assert is_valid_email(email)


@pytest.mark.parametrize(
    "email",
    [
        "@example.com",
        "a@b@example.com",
        "x y@example.com",
        "anna.demo",
        "anna@example",
        "anna@.com",
        "søren@example.com",
        "",
        None,
    ],
)
def test_invalid_email(email):
    assert not is_valid_email(email)
