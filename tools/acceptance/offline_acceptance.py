#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List


@dataclass(frozen=True)
class CheckResult:
    check_id: str
    ok: bool
    message: str


def _repo_root() -> Path:
    return Path(os.getcwd()).resolve()


def _load_manifest(path: Path) -> Dict[str, Any]:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def _check_file_exists(root: Path, path: str) -> bool:
    return (root / path).is_file()


def _check_paths_exist(root: Path, paths: List[str]) -> List[str]:
    missing: List[str] = []
    for p in paths:
        if not (root / p).exists():
            missing.append(p)
    return missing


def _check_version_semver(root: Path) -> CheckResult:
    version_path = root / "VERSION"
    if not version_path.is_file():
        return CheckResult("m0_version_semver", False, "VERSION file missing")
    raw = version_path.read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?", raw):
        return CheckResult("m0_version_semver", False, f"VERSION not semver: {raw!r}")
    return CheckResult("m0_version_semver", True, f"VERSION={raw}")


def run(manifest_path: Path) -> List[CheckResult]:
    root = _repo_root()
    manifest = _load_manifest(manifest_path)

    results: List[CheckResult] = []

    for item in manifest.get("checks", []):
        check_id = item.get("id", "unknown")
        ctype = item.get("type")

        if ctype == "file_exists":
            p = item.get("path", "")
            ok = bool(p) and _check_file_exists(root, p)
            msg = "ok" if ok else f"missing file: {p}"
            results.append(CheckResult(check_id, ok, msg))
            continue

        if ctype == "paths_exist":
            paths = item.get("paths", [])
            missing = _check_paths_exist(root, paths)
            ok = len(missing) == 0
            msg = "ok" if ok else ("missing paths: " + ", ".join(missing))
            results.append(CheckResult(check_id, ok, msg))
            continue

        results.append(CheckResult(check_id, False, f"unsupported check type: {ctype!r}"))

    # Add implicit checks.
    results.append(_check_version_semver(root))

    return results


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", required=True)
    ap.add_argument("--report", default="acceptance/reports/offline.json")
    args = ap.parse_args()

    root = _repo_root()
    manifest_path = (root / args.manifest).resolve()
    if not manifest_path.is_file():
        print(f"[acceptance] manifest not found: {manifest_path}", file=sys.stderr)
        return 2

    results = run(manifest_path)

    ok = all(r.ok for r in results)
    report = {
        "ok": ok,
        "results": [r.__dict__ for r in results],
    }

    report_path = (root / args.report).resolve()
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    for r in results:
        prefix = "PASS" if r.ok else "FAIL"
        print(f"[acceptance] {prefix} {r.check_id}: {r.message}")

    if not ok:
        print(f"[acceptance] report written: {report_path}", file=sys.stderr)
        return 1

    print(f"[acceptance] OK (report: {report_path})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
