#!/usr/bin/env python3

from __future__ import annotations

import os
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Set, Tuple


@dataclass(frozen=True)
class Project:
    name: str
    path: Path
    layer: str
    references: Tuple[str, ...]


def _repo_root() -> Path:
    return Path(os.getcwd()).resolve()


def _discover_csprojs(root: Path) -> List[Path]:
    return sorted((root / "src").glob("**/*.csproj"))


def _project_name(csproj: Path) -> str:
    return csproj.stem


def _layer_for(csproj: Path) -> str:
    # Convention over configuration (DDD layers)
    # Directory name examples:
    # - src/PzManager.Domain/...
    # - src/PzManager.Application/...
    dir_name = csproj.parent.name.lower()
    for candidate in (
        "Domain",
        "Application",
        "Infrastructure",
        "Transport",
        "Manager",
        "Client.Cli",
        "Client",
    ):
        if dir_name.endswith(candidate.lower()):
            return candidate
    return "Unknown"


def _parse_project_references(csproj: Path) -> List[Path]:
    tree = ET.parse(csproj)
    root = tree.getroot()

    # Handle default namespace-less MSBuild XML
    refs: List[Path] = []
    for elem in root.findall(".//ProjectReference"):
        include = elem.attrib.get("Include")
        if not include:
            continue
        include = include.replace("\\", "/")
        refs.append((csproj.parent / include).resolve())
    return refs


def _build_projects(root: Path) -> Dict[str, Project]:
    csprojs = _discover_csprojs(root)
    by_name: Dict[str, Project] = {}

    path_to_name: Dict[Path, str] = {}
    for p in csprojs:
        path_to_name[p.resolve()] = _project_name(p)

    for p in csprojs:
        refs = _parse_project_references(p)
        ref_names: List[str] = []
        for rp in refs:
            n = path_to_name.get(rp)
            if n:
                ref_names.append(n)
            else:
                # External project reference (unexpected but allow for now)
                ref_names.append(rp.stem)

        proj = Project(
            name=_project_name(p),
            path=p,
            layer=_layer_for(p),
            references=tuple(sorted(ref_names)),
        )
        by_name[proj.name] = proj

    return by_name


def _allowed_dependencies() -> Dict[str, Set[str]]:
    # Allowed project-level references by layer.
    # (Host layer can depend on anything.)
    return {
        "Domain": set(),
        "Application": {"PzManager.Domain"},
        "Infrastructure": {"PzManager.Domain", "PzManager.Application"},
        "Transport": {"PzManager.Domain", "PzManager.Application"},
        "Manager": {"PzManager.Domain", "PzManager.Application", "PzManager.Infrastructure", "PzManager.Transport"},
        "Client": {"PzManager.Domain", "PzManager.Application", "PzManager.Transport"},
        "Client.Cli": {"PzManager.Client", "PzManager.Domain", "PzManager.Application", "PzManager.Transport"},
        "Unknown": set(),
    }


def _check_project_graph(projects: Dict[str, Project]) -> List[str]:
    allowed = _allowed_dependencies()
    errors: List[str] = []

    for p in projects.values():
        allow = allowed.get(p.layer, set())
        for r in p.references:
            if r not in allow:
                errors.append(f"{p.name}({p.layer}) must not reference {r}")

    return errors


def _check_domain_no_godot(root: Path) -> List[str]:
    domain_dir = root / "src" / "PzManager.Domain"
    if not domain_dir.exists():
        return []

    errors: List[str] = []
    for cs in domain_dir.glob("**/*.cs"):
        text = cs.read_text(encoding="utf-8")
        if re.search(r"\busing\s+Godot\b", text) or re.search(r"\bGodot\.", text):
            errors.append(f"Domain must not depend on Godot: {cs}")
    return errors


def main() -> int:
    root = _repo_root()
    if not (root / "src").exists():
        print("[archcheck] src/ missing", file=sys.stderr)
        return 2

    projects = _build_projects(root)
    errors = _check_project_graph(projects) + _check_domain_no_godot(root)

    if errors:
        for e in errors:
            print(f"[archcheck] FAIL: {e}", file=sys.stderr)
        return 1

    print(f"[archcheck] OK ({len(projects)} projects)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
