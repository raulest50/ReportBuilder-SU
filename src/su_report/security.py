from __future__ import annotations

import hashlib
import json
import os
import re
from pathlib import Path
from typing import Any

from su_report.settings import SECRET_KEYS


_AUTHORIZATION = re.compile(r"(?i)(authorization\s*:\s*)[^\r\n]+")
_BASIC_VALUE = re.compile(r"(?i)(basic\s+)[A-Za-z0-9+/=]{8,}")
_SECRET_ASSIGNMENT = re.compile(
    r"(?i)(\b(?:USER_CUBO_SICOM|PASSWORD_CUBO_SICOM|CUBO_LIQS_URL|CUBO_AGENTS_URL|SOCRATA_APP_TOKEN)\b"
    r"\s*[=:]\s*)(?:\"[^\"]*\"|'[^']*'|[^\s,;]+)"
)


def sanitize(value: str) -> str:
    sanitized = value
    for key in (*SECRET_KEYS, "SOCRATA_APP_TOKEN"):
        secret = os.environ.get(key)
        if secret:
            sanitized = sanitized.replace(secret, "***")
    sanitized = _SECRET_ASSIGNMENT.sub(r"\1[REDACTED]", sanitized)
    sanitized = _AUTHORIZATION.sub(r"\1***", sanitized)
    return _BASIC_VALUE.sub(r"\1***", sanitized)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)
