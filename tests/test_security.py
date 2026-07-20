from su_report.security import sanitize


def test_sanitize_masks_credentials_and_authorization() -> None:
    message = "PASSWORD_CUBO_SICOM=secreto Authorization: Basic abc USER_CUBO_SICOM=usuario"

    sanitized = sanitize(message)

    assert "secreto" not in sanitized
    assert "abc" not in sanitized
    assert "usuario" not in sanitized
    assert "[REDACTED]" in sanitized
