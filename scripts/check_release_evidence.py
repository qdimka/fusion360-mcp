#!/usr/bin/env python3
"""Validate final Fusion E2E evidence and hashes before a tagged release."""
from __future__ import annotations

import argparse
import hashlib
import re
from pathlib import Path

REQUIRED_FIELDS = (
    "Status",
    "Windows",
    "macOS",
    "Candidate version",
    "Candidate source commit",
    "Add-in SHA256",
)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _read_fields(path: Path) -> dict[str, str]:
    fields: dict[str, str] = {}
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if ":" not in line:
            continue
        key, value = line.split(":", 1)
        key, value = key.strip(), value.strip()
        if key in REQUIRED_FIELDS or key.startswith("Server ") and key.endswith(" SHA256"):
            if key in fields:
                raise ValueError(f"Duplicate evidence field {key!r} on line {number}")
            fields[key] = value
    return fields


def check_release_evidence(
    path: Path,
    version: str,
    addin_zip: Path,
    server_archives: dict[str, Path] | None = None,
) -> None:
    """Raise ValueError unless evidence and supplied artifact hashes agree."""
    fields = _read_fields(path)
    missing = [field for field in REQUIRED_FIELDS if not fields.get(field)]
    if missing:
        raise ValueError(f"Missing required evidence fields: {', '.join(missing)}")

    expected_status = {
        "Status": "pass",
        "Windows": "deferred (post-release)",
        "macOS": "pass",
        "Candidate version": version,
    }
    for field, expected in expected_status.items():
        if fields[field] != expected:
            raise ValueError(f"{field} must be {expected!r}, found {fields[field]!r}")

    source_commit = fields["Candidate source commit"]
    if not re.fullmatch(r"[0-9a-fA-F]{40,64}", source_commit):
        raise ValueError("Candidate source commit must be a full Git commit hash")

    addin_hash = fields["Add-in SHA256"]
    if not re.fullmatch(r"[0-9a-fA-F]{64}", addin_hash):
        raise ValueError("Add-in SHA256 must contain 64 hexadecimal characters")
    actual_addin_hash = _sha256(addin_zip)
    if addin_hash.lower() != actual_addin_hash:
        raise ValueError(
            f"Add-in SHA256 mismatch: evidence has {addin_hash}, artifact is {actual_addin_hash}"
        )

    for rid, archive in (server_archives or {}).items():
        if not re.fullmatch(r"(?:win-x64|osx-(?:arm64|x64)|linux-x64)", rid):
            raise ValueError(f"Unsupported server runtime identifier: {rid}")
        field = f"Server {rid} SHA256"
        expected_hash = fields.get(field)
        if not expected_hash or not re.fullmatch(r"[0-9a-fA-F]{64}", expected_hash):
            raise ValueError(f"Missing or invalid {field} evidence")
        actual_hash = _sha256(archive)
        if expected_hash.lower() != actual_hash:
            raise ValueError(
                f"{field} mismatch: evidence has {expected_hash}, artifact is {actual_hash}"
            )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--addin-zip", type=Path, required=True)
    parser.add_argument(
        "--server",
        action="append",
        default=[],
        metavar="RID=PATH",
        help="server archive to verify (repeatable)",
    )
    args = parser.parse_args()
    archives: dict[str, Path] = {}
    for item in args.server:
        try:
            rid, path = item.split("=", 1)
        except ValueError as exc:
            parser.error(f"invalid --server {item!r}; expected RID=PATH")
        if rid in archives:
            parser.error(f"duplicate --server runtime identifier {rid!r}")
        archives[rid] = Path(path)
    try:
        check_release_evidence(args.evidence, args.version, args.addin_zip, archives)
    except (OSError, ValueError) as exc:
        parser.error(str(exc))
    print("Release evidence matches candidate artifacts")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
